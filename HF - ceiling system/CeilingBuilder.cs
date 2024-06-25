using System.Collections.Generic;
using Code.GameManagers;
using Empyrean.HouseFlipper;
using Empyrean.HouseFlipper.HouseManagement;
using FrozenWay.HouseFlipper.Features.StoreyStairs;
using UnityEngine;

namespace Code.HouseBuildingBlocks {
    public static class CeilingBuilder {

        public static Ceiling CreateCeiling(StoreyBuilder storeyBuilder, out SubcombinerObject subcombinerObject) {
            if (!storeyBuilder) {
                subcombinerObject = null;
                return null;
            }

            subcombinerObject = CreateCeiling(storeyBuilder, out Ceiling ceiling);
            return ceiling;
        }

        public static Ceiling CreateCeilingFromSubceilings(StoreyBuilder storeyBuilder, List<GameObject> subceilings) {
            if (!storeyBuilder || subceilings == null)
                return null;

            var ceiling = CreateCeiling(storeyBuilder, out SubcombinerObject subcombinerObject);
            ceiling.transform.position = Vector3.zero;
            var combineInstances = GetCombineInstancesFromSubceilings(subceilings);
            var combinedCeilingsMesh = new Mesh();

            combinedCeilingsMesh.CombineMeshes(combineInstances.ToArray());
            subcombinerObject.SharedMesh = combinedCeilingsMesh;
            subcombinerObject.RefreshMeshCollider();
            return ceiling;
        }

        public static void BuildCeiling(Ceiling ceiling, SubcombinerObject subcombinerObject, Material originalMaterial = null) {
            if (!ceiling || ceiling.CannotBeCombined || !subcombinerObject)
                return;

            var standardCeilingHeight = (int)ceiling.Storey + Consts.OUTER_WALL_HEIGHT;

            if (IsMaterialInvisible(originalMaterial, out var newMaterial)) 
                originalMaterial = newMaterial;

            var ceilingSubcombiner = originalMaterial != null? new StoreyCeilingSubcombiner(subcombinerObject, standardCeilingHeight, originalMaterial) : new StoreyCeilingSubcombiner(subcombinerObject, standardCeilingHeight);
            BuildCeiling(ceiling, ceilingSubcombiner);
        }

        static bool IsMaterialInvisible(Material originalMaterial, out Material newMaterial) {
            newMaterial = originalMaterial;
            if (!GameManager.House || !GameManager.House.HouseBuilder)
                return false;
            var houseBuildingBlocks = GameManager.House.HouseBuilder.BuildingBlocks;
            if (originalMaterial == houseBuildingBlocks.InvisibleMaterial) {
                newMaterial = houseBuildingBlocks.CeilingMaterial;
                return true;
            }
            return false;
        }

        public static void BuildCeiling(Ceiling ceiling) {
            if(!ceiling || ceiling.CannotBeCombined)
                return;

            var combiner = new StoreyCeilingSubcombiner(ceiling);
            BuildCeiling(ceiling, combiner);
        }

        public static void CutTiles(Ceiling ceiling, IReadOnlyList<Floor> floorTilesToCut) {
            if (CreateAndBuildSubcombiner(ceiling, out var combiner))
                return;

            combiner.RemoveTiles(floorTilesToCut);
            combiner.Build();
        }

        public static void AddTiles(Ceiling ceiling, IReadOnlyList<Floor> floorTilesToFillCeiling) {
            if (CreateAndBuildSubcombiner(ceiling, out var combiner))
                return;

            combiner.AddTiles(floorTilesToFillCeiling);
            combiner.Build();
        }

        public static GameObject FindCeilingObject(Transform parent) {
            var ceilingLayerMask = LayerMask.NameToLayer(Consts.Layers.CEILING);
            foreach (Transform child in parent) {
                if (child.gameObject.layer == ceilingLayerMask || child.CompareTag(Consts.Tags.CEILING)) {
                    return child.gameObject;
                }
            }

            return null;
        }

        public static List<GameObject> GetSubceilings(GameObject subceilingsContainer) {
            bool MayHaveSubceilings() => subceilingsContainer.transform.childCount > 0;

            var subceilings = new List<GameObject>();
            if (MayHaveSubceilings()) {
                foreach (Transform ceilingChild in subceilingsContainer.transform) {
                    if (ceilingChild.gameObject.layer == subceilingsContainer.layer || ceilingChild.CompareTag(Consts.Tags.CEILING)) {
                        subceilings.Add(ceilingChild.gameObject);
                    }
                }
            }

            return subceilings;
        }

        static SubcombinerObject CreateCeiling(StoreyBuilder storeyBuilder, out Ceiling ceiling) {
            var ceilingObject = Object.Instantiate(CoreAsset.Singlasset.subcombinerObjectPrefab, storeyBuilder.transform, true);
            var standardCeilingHeight = (int)storeyBuilder.storeyType + Consts.OUTER_WALL_HEIGHT;

            ceiling = ceilingObject.AddOrGetComponent<Ceiling>();
            var subcombiner = new StoreyCeilingSubcombiner(ceilingObject, standardCeilingHeight);
            BuildCeiling(ceiling, subcombiner);
            return ceilingObject;
        }

        static void BuildCeiling(Ceiling ceiling, StoreyCeilingSubcombiner ceilingSubcombiner) {
            ceiling.CeilingSubcombiner = ceilingSubcombiner;
            CombineFloorsToCeiling(ceiling, ceilingSubcombiner);
            ceiling.Setup();
        }

        static void CombineFloorsToCeiling(Ceiling ceiling, StoreyCeilingSubcombiner ceilingSubcombiner) {
            var floorsContainerAboveCeiling = GetFloorTilesContainerAbove(ceiling);
            var floorsContainerBelowCeiling = GetFloorTilesContainerBelow(ceiling);
            var activeFloorsUnion = MakeActiveStoreysFloorUnion(floorsContainerAboveCeiling, floorsContainerBelowCeiling);
            foreach (var forcedPosition in ceiling.ForcedPositions)
                activeFloorsUnion.Add(forcedPosition);

            AddUnionTilesToCombiner(activeFloorsUnion, ceiling, ceilingSubcombiner);
            ceilingSubcombiner.Build();
        }

        static bool CreateAndBuildSubcombiner(Ceiling ceiling, out StoreyCeilingSubcombiner combiner) {
            combiner = ceiling.CeilingSubcombiner;
            if (combiner == null) {
                BuildCeiling(ceiling);
                return true;
            }

            if (!combiner.HasSubcombinerObject) {
                var subcombinerObject = ceiling.AddOrGetComponent<SubcombinerObject>();
                combiner.SetSubcombinerObject(subcombinerObject);
            }

            return false;
        }

        static List<CombineInstance> GetCombineInstancesFromSubceilings(List<GameObject> subceilings) {
            const bool newStaticState = false;
            var combineInstances = new List<CombineInstance>();

            foreach (var subceiling in subceilings) {
                var meshFilter = subceiling.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null) {
                    subceiling.isStatic = newStaticState;
                    var combine = new CombineInstance {
                        mesh = meshFilter.sharedMesh,
                        transform = meshFilter.transform.localToWorldMatrix
                    };
                    combineInstances.Add(combine);
                }
            }

            return combineInstances;
        }

        static HashSet<Vector3> MakeActiveStoreysFloorUnion(Transform floorsContainerAbove, Transform floorsContainerBelow) {
            var halfTileSize = Floor.TILE_SIZE / 2;
            var comparer = new Vector3Comparer(halfTileSize);
            var storeysFloorUnion = new HashSet<Vector3>(comparer);
            var disabledFloors = new HashSet<Vector3>(comparer);

            if (floorsContainerAbove != null)
                AddFloorsFromAbove(floorsContainerAbove, disabledFloors, storeysFloorUnion);
            AddFloorsFromBelow(floorsContainerBelow, disabledFloors, storeysFloorUnion);

            return storeysFloorUnion;
        }

        static void AddFloorsFromAbove(Transform floorsContainerAbove, HashSet<Vector3> disabledFloors, HashSet<Vector3> storeysFloorUnion) {
            foreach (Transform floorTile in floorsContainerAbove) {
                if (!floorTile.gameObject.activeSelf) {
                    disabledFloors.Add(floorTile.position);
                    continue;
                }

                storeysFloorUnion.Add(floorTile.position);
            }
        }

        static void AddFloorsFromBelow(Transform sameFloorsContainer, HashSet<Vector3> disabledFloors, HashSet<Vector3> storeysFloorUnion) {
            if (sameFloorsContainer.childCount == 0)
                return;

            var firstChild = sameFloorsContainer.GetChild(0);
            if (!firstChild.position.y.ToStoreyType().IsNextStoreyDefined(out StoreyBuilder.StoreyType storeyAbove))
                return;

            float floorsAboveHeight = (int)storeyAbove;
            foreach (Transform floorTile in sameFloorsContainer) {
                if (!floorTile.gameObject.activeSelf)
                    continue;

                var tilePosition = floorTile.position;
                Vector3 toUpperFloor = new Vector3(tilePosition.x, floorsAboveHeight, tilePosition.z);
               
                var floorGridCoords = HFGrid.TileBLCornerToGridCoords(tilePosition);
                var architectFloorsDictionary = GameManager.Instance.houseBuilderSavingManager.ArchitectFloorsDictionary;
                
                if (disabledFloors.Contains(toUpperFloor))
                    continue;
                
                if (architectFloorsDictionary.ContainsKey(floorGridCoords) && architectFloorsDictionary[floorGridCoords].isAtRooftop)
                    continue;
          
                storeysFloorUnion.Add(toUpperFloor);
            }
        }

        static void AddUnionTilesToCombiner(HashSet<Vector3> floorsUnion, Ceiling ceiling, StoreyCeilingSubcombiner combiner) {
            foreach (var floorTile in floorsUnion) {
                if (ceiling.IsRestrictedPosition(floorTile))
                    continue;

                var coords = HFGrid.TileBLCornerToGridCoords(floorTile);
                combiner.AddTile(coords.GridCoordsToTileCorner());
            }
        }

        static Transform GetFloorTilesContainerAbove(Ceiling ceiling) {
            if (!ceiling.Storey.IsNextStoreyDefined(out var nextStorey)) 
                return null;
            
            var storey = GameManager.House?.GetStoreyBuilder(nextStorey);
            return storey == null ? null : storey.floorsMapBuilder.transform;
        }

        static Transform GetFloorTilesContainerBelow(Ceiling ceiling) {
            var storey = GameManager.House.GetStoreyBuilder(ceiling.Storey);
            return storey.floorsMapBuilder.transform;
        }
    } 
}
