﻿namespace Mapbox.Unity.MeshGeneration.Interfaces
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Linq;
	using Mapbox.Unity.MeshGeneration.Data;
	using Mapbox.Unity.MeshGeneration.Modifiers;
	using Mapbox.VectorTile;
	using UnityEngine;
	using Mapbox.Unity.Map;
	using Mapbox.Unity.Utilities;

	public class PolygonLayerVisualizer : LayerVisualizerBase
	{
		VectorSubLayerProperties _layerProperties;
		LayerPerformanceOptions _performanceOptions;
		private Dictionary<UnityTile, List<int>> _activeCoroutines;
		int _entityInCurrentCoroutine = 0;

		ModifierStackBase _defaultStack;

		private string _key;
		public override string Key
		{
			get { return _layerProperties.coreOptions.layerName; }
			set { _layerProperties.coreOptions.layerName = value; }
		}
		public void SetProperties(VectorSubLayerProperties properties, LayerPerformanceOptions performanceOptions)
		{
			List<MeshModifier> defaultMeshModifierStack = new List<MeshModifier>();
			List<GameObjectModifier> defaultGOModifierStack = new List<GameObjectModifier>();

			_layerProperties = properties;
			_performanceOptions = performanceOptions;

			Active = _layerProperties.coreOptions.isActive;

			if (properties.coreOptions.groupFeatures)
			{
				_defaultStack = ScriptableObject.CreateInstance<MergedModifierStack>();
			}
			else
			{
				_defaultStack = ScriptableObject.CreateInstance<ModifierStack>();
			}

			_defaultStack.MeshModifiers = new List<MeshModifier>();
			_defaultStack.GoModifiers = new List<GameObjectModifier>();

			switch (properties.coreOptions.geometryType)
			{
				case VectorPrimitiveType.Point:
					if (_layerProperties.coreOptions.snapToTerrain == true)
					{
						defaultMeshModifierStack.Add(CreateInstance<SnapTerrainModifier>());
					}
					break;
				case VectorPrimitiveType.Line:
					if (_layerProperties.coreOptions.snapToTerrain == true)
					{
						defaultMeshModifierStack.Add(CreateInstance<SnapTerrainModifier>());
					}
					defaultMeshModifierStack.Add(CreateInstance<LineMeshModifier>());
					//defaultMeshModifierStack.Add(CreateInstance<UvModifier>());
					if (_layerProperties.extrusionOptions.extrusionType != Map.ExtrusionType.None)
					{
						var heightMod = CreateInstance<HeightModifier>();
						heightMod.SetProperties(_layerProperties.extrusionOptions);
						defaultMeshModifierStack.Add(heightMod);
					}

					var lineMatMod = CreateInstance<MaterialModifier>();
					lineMatMod.SetProperties(_layerProperties.materialOptions);
					defaultGOModifierStack.Add(lineMatMod);
					break;
				case VectorPrimitiveType.Polygon:
					if (_layerProperties.coreOptions.snapToTerrain == true)
					{
						defaultMeshModifierStack.Add(CreateInstance<SnapTerrainModifier>());
					}
					defaultMeshModifierStack.Add(CreateInstance<PolygonMeshModifier>());
					defaultMeshModifierStack.Add(CreateInstance<UvModifier>());
					if (_layerProperties.extrusionOptions.extrusionType != Map.ExtrusionType.None)
					{
						var heightMod = CreateInstance<HeightModifier>();
						heightMod.SetProperties(_layerProperties.extrusionOptions);
						defaultMeshModifierStack.Add(heightMod);
					}

					var matMod = CreateInstance<MaterialModifier>();
					matMod.SetProperties(_layerProperties.materialOptions);
					defaultGOModifierStack.Add(matMod);

					break;
				default:
					break;
			}

			_defaultStack.MeshModifiers.AddRange(defaultMeshModifierStack);
			_defaultStack.GoModifiers.AddRange(defaultGOModifierStack);

			//Add any additional modifiers that were added. 
			_defaultStack.MeshModifiers.AddRange(_layerProperties.modifierOptions.MeshModifiers);
			_defaultStack.GoModifiers.AddRange(_layerProperties.modifierOptions.GoModifiers);

		}
		public override void Initialize()
		{
			base.Initialize();
			_entityInCurrentCoroutine = 0;
			_activeCoroutines = new Dictionary<UnityTile, List<int>>();

			//foreach (var filter in Filters)
			//{
			//	if (filter != null)
			//	{
			//		filter.Initialize();
			//	}
			//}

			if (_defaultStack != null)
			{
				_defaultStack.Initialize();
			}

			//foreach (var item in Stacks)
			//{
			//	if (item != null && item.Stack != null)
			//	{
			//		item.Types = item.Type.Split(',');
			//		item.Stack.Initialize();
			//	}
			//}
		}

		public override void Create(VectorTileLayer layer, UnityTile tile, Action callback)
		{
			if (!_activeCoroutines.ContainsKey(tile))
				_activeCoroutines.Add(tile, new List<int>());
			_activeCoroutines[tile].Add(Runnable.Run(ProcessLayer(layer, tile, callback)));
		}

		private IEnumerator ProcessLayer(VectorTileLayer layer, UnityTile tile, Action callback = null)
		{
			//HACK to prevent request finishing on same frame which breaks modules started/finished events 
			yield return null;

			if (tile == null)
			{
				yield break;
			}

			//testing each feature with filters
			var fc = layer.FeatureCount();
			//Get all filters in the array. 
			var filters = _layerProperties.coreOptions.filters.Select(m => m.GetFilterComparer()).ToArray();

			// Pass them to the combiner 
			Filters.ILayerFeatureFilterComparer combiner = new Filters.LayerFilterComparer();
			switch (_layerProperties.coreOptions.combinerType)
			{
				case Filters.LayerFilterCombinerOperationType.Any:
					combiner = Filters.LayerFilterComparer.AnyOf(filters);
					break;
				case Filters.LayerFilterCombinerOperationType.All:
					combiner = Filters.LayerFilterComparer.AllOf(filters);
					break;
				case Filters.LayerFilterCombinerOperationType.None:
					combiner = Filters.LayerFilterComparer.NoneOf(filters);
					break;
				default:
					break;
			}

			for (int i = 0; i < fc; i++)
			{
				var feature = new VectorFeatureUnity(layer.GetFeature(i, 0), tile, layer.Extent);

				if (filters.Length == 0)
				{
					// no filters, just build the features. 
					if (tile != null && tile.gameObject != null && tile.VectorDataState != Enums.TilePropertyState.Cancelled)
						Build(feature, tile, tile.gameObject);

					_entityInCurrentCoroutine++;
				}
				else
				{
					// build features only if the filter returns true. 
					if (combiner.Try(feature))
					{
						if (tile != null && tile.gameObject != null && tile.VectorDataState != Enums.TilePropertyState.Cancelled)
							Build(feature, tile, tile.gameObject);

						_entityInCurrentCoroutine++;
					}
				}

				if (_performanceOptions.isEnabled && _entityInCurrentCoroutine >= _performanceOptions.entityPerCoroutine)
				{
					_entityInCurrentCoroutine = 0;
					yield return null;
				}
			}

			var mergedStack = _defaultStack as MergedModifierStack;
			if (mergedStack != null && tile != null)
			{
				mergedStack.End(tile, tile.gameObject, layer.Name);
			}

			//foreach (var item in Stacks)
			//{
			//	mergedStack = item.Stack as MergedModifierStack;
			//	if (mergedStack != null)
			//	{
			//		mergedStack.End(tile, tile.gameObject, layer.Name);
			//	}
			//}

			if (callback != null)
				callback();
		}

		/// <summary>
		/// Preprocess features, finds the relevant modifier stack and passes the feature to that stack
		/// </summary>
		/// <param name="feature"></param>
		/// <param name="tile"></param>
		/// <param name="parent"></param>
		private bool IsFeatureValid(VectorFeatureUnity feature)
		{
			if (feature.Properties.ContainsKey("extrude") && !bool.Parse(feature.Properties["extrude"].ToString()))
				return false;

			if (feature.Points.Count < 1)
				return false;

			return true;
		}

		private void Build(VectorFeatureUnity feature, UnityTile tile, GameObject parent)
		{
			if (feature.Properties.ContainsKey("extrude") && !Convert.ToBoolean(feature.Properties["extrude"]))
				return;

			if (feature.Points.Count < 1)
				return;

			//this will be improved in next version and will probably be replaced by filters
			var styleSelectorKey = FindSelectorKey(feature);

			var meshData = new MeshData();
			meshData.TileRect = tile.Rect;

			//and finally, running the modifier stack on the feature
			var processed = false;
			//for (int i = 0; i < Stacks.Count; i++)
			//{
			//	foreach (var key in Stacks[i].Types)
			//	{
			//		if (key == styleSelectorKey)
			//		{
			//			processed = true;
			//			Stacks[i].Stack.Execute(tile, feature, meshData, parent, styleSelectorKey);
			//			break;
			//		}
			//	}

			//	if (processed)
			//		break;
			//}
			if (!processed)
			{
				if (_defaultStack != null)
				{
					_defaultStack.Execute(tile, feature, meshData, parent, styleSelectorKey);
				}
			}
		}

		private string FindSelectorKey(VectorFeatureUnity feature)
		{
			//if (string.IsNullOrEmpty(_classificationKey))
			//{
			//	if (feature.Properties.ContainsKey("type"))
			//	{
			//		return feature.Properties["type"].ToString().ToLowerInvariant();
			//	}
			//	else if (feature.Properties.ContainsKey("class"))
			//	{
			//		return feature.Properties["class"].ToString().ToLowerInvariant();
			//	}
			//}
			//else 
			//TODO: Come back to this. 
			//var size = _layerProperties.coreOptions.propertyValuePairs.Count;
			//for (int i = 0; i < size; i++)
			//{
			//	var key = _layerProperties.coreOptions.propertyValuePairs[i].featureKey;
			//	if (feature.Properties.ContainsKey(key))
			//	{
			//		if (feature.Properties.ContainsKey(key))
			//		{
			//			return feature.Properties[key].ToString().ToLowerInvariant();
			//		}
			//	}
			//}


			return Key;
		}

		/// <summary>
		/// Handle tile destruction event and propagate it to modifier stacks
		/// </summary>
		/// <param name="tile">Destroyed tile object</param>
		public override void OnUnregisterTile(UnityTile tile)
		{
			base.OnUnregisterTile(tile);
			tile.VectorDataState = Enums.TilePropertyState.Cancelled;
			if (_activeCoroutines.ContainsKey(tile))
			{
				foreach (var cor in _activeCoroutines[tile])
				{
					Runnable.Stop(cor);
				}
			}

			if (_defaultStack != null)
				_defaultStack.UnregisterTile(tile);
			//foreach (var val in Stacks)
			//{
			//	if (val != null && val.Stack != null)
			//		val.Stack.UnregisterTile(tile);
			//}
		}
	}
}