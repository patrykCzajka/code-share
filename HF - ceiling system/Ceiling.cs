using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Player.Tools;
using Code.FloorToRooms.Tiles;
using Code.GameManagers;
using Empyrean.HouseFlipper;
using Empyrean.HouseFlipper.HouseManagement;
using Features.Farm.HouseBuilder.Code;
using FrozenWay.HouseFlipper.Features.StoreyStairs;
using FrozenWay.HouseFlipper.Pets.PetsWalkableArea;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace Code.HouseBuildingBlocks {
    public class Ceiling : SceneSystemSavingHelper, ISerializationCallbackReceiver {
        public const string HIERARCHY_NAME = "Ceiling";
        const float TILE_TO_CEILING_Y_OFFSET = 0.001F;
        const float GET_CEILING_TILE_CHECK_MAX_DIST = 0.3f;
        const float GET_CEILING_TILE_MARGIN = 0.1f;

        readonly HashSet<CeilingTile> currentTiles = new HashSet<CeilingTile>();
        readonly HashSet<CeilingTile> tmpCreatedTiles = new HashSet<CeilingTile>();
        readonly Quaternion tileRotation = Quaternion.Euler(-90, 0, 180);
        readonly RaycastHit[] results = new RaycastHit[1];
        readonly Vector3 architectRooftopOffset = new Vector3(0f, Consts.STOREY_HEIGHT, 0f);

        [SerializeField, HideInInspector] public StoreyCeilingSubcombiner CeilingSubcombiner;
        [SerializeField, HideInInspector] StoreyBuilder storeyBuilder;
        [SerializeField, HideInInspector] CeilingTilesCombiner tilesCombiner;
        [SerializeField, ReadOnly] bool isSetup;

        [SerializeField, ReadOnly, FoldoutGroup("Data")] 
        readonly List<CeilingModification> initialCeilingModifications = new List<CeilingModification>();
        [SerializeField, ReadOnly, FoldoutGroup("Data")] 
        StoreyBuilder.StoreyType storeyType;
        [SerializeField, ReadOnly, FoldoutGroup("Data")] 
        Transform tilesParent;
        [SerializeField, ReadOnly, FoldoutGroup("Data")] 
        Transform combinerParent;
        [SerializeField, ReadOnly, FoldoutGroup("Data")] 
        CeilingTile tilePrefab;
        [SerializeField, ReadOnly, FoldoutGroup("Data")] 
        float globalYPos;
        [SerializeField, ReadOnly, FoldoutGroup("Data")] 
        Material material;
        [SerializeField, ReadOnly, FoldoutGroup("Data")] 
        CeilingTile previewTile;

        [SerializeField, FoldoutGroup("Settings")] 
        InteractionSettings interactionSettings;
        [SerializeField, FoldoutGroup("Settings")] 
        bool splitSceneCeiling;
        [SerializeField, FoldoutGroup("Settings")]
        bool cannotBeCombined;

        HashSet<Vector3> restrictedPositions;
        HashSet<Vector3> forcedPositions;
        Dictionary<Vector3, CeilingTile> positionToTile;
        bool readyForTileCollect;

        HouseBuilderSavingManager houseBuilderSavingManager => GameManager.Instance.houseBuilderSavingManager;

#if UNITY_EDITOR
        [SerializeField] [HideInPlayMode] Material materialSlotHelper;
#endif

        public bool BlockStairsPlacement => cannotBeCombined || interactionSettings.blockStairsPlacement;
        public bool BlockTilingAndPainting => interactionSettings.blockTilingAndPainting;
        public bool IsSetup => isSetup;
        public CeilingTile PreviewTile => previewTile;
        public Material Material => material;
        public StoreyBuilder.StoreyType Storey => storeyType;
        public bool IsCombined => cannotBeCombined || CeilingSubcombiner != null && CeilingSubcombiner.HasMesh();
        public bool CannotBeCombined {
            get => cannotBeCombined;
            set => cannotBeCombined = value;
        }

        public HashSet<Vector3> ForcedPositions => forcedPositions;

        [Button]
        void PrintForcedPositions() {
            foreach (var VARIABLE in ForcedPositions) {
                Debug.Log(VARIABLE);
            }
        }

        void Awake() {
            HFEvents.OnToolChanged += HandleToolChanged;
            HFEvents.HouseFreshStart += HandleFreshStart;
            HFEvents.HouseChanged += HandleLateInit;
            if (splitSceneCeiling)
                HFEvents.HouseInit += RebuildCeiling;

            var tilesComparer = new Vector3Comparer(Consts.FLOAT_COMPARE_PRECISION);
            restrictedPositions = new HashSet<Vector3>(tilesComparer);
            forcedPositions = new HashSet<Vector3>(tilesComparer);
            positionToTile = new Dictionary<Vector3, CeilingTile>(tilesComparer);
        }

        void HandleLateInit(string _) {
            if(splitSceneCeiling)
                HFEvents.BroadcastCeilingSplitSceneCeilingLoaded(this);
        }

        void OnDestroy() {
            if(positionToTile != null)
                foreach (var ceilingTilePosition in positionToTile) {
                    if (ceilingTilePosition.Value != null && ceilingTilePosition.Value.WallSide != null)
                        ceilingTilePosition.Value.OnMaterialChanged -= OnTileMaterialChanged;
                }

            if (combinerParent != null)
                Destroy(combinerParent.gameObject);

            HFEvents.OnToolChanged -= HandleToolChanged;
            HFEvents.HouseFreshStart -= HandleFreshStart;
            HFEvents.HouseChanged -= HandleLateInit;
        }

        public void ClearRefsForGC() {
            currentTiles?.Clear();
            tmpCreatedTiles?.Clear();
            CeilingSubcombiner = null;
            storeyBuilder = null;
            tilesCombiner = null;
            positionToTile?.Clear();
            restrictedPositions?.Clear();
            forcedPositions?.Clear();
        }

        public void OnBeforeSerialize() {
            if (CeilingSubcombiner != null)
                CeilingSubcombiner.OnBeforeSerialize();
        }

        public void OnAfterDeserialize() {
            if (tilesCombiner != null)
                CeilingSubcombiner.OnAfterDeserialize();
        }

        public void SetMaterial(Material material) {
            this.material = material;
        }

        public float GetCeilingYPos() {
            return globalYPos;
        }

        public float GetBoundsYPos() {
            return globalYPos + TILE_TO_CEILING_Y_OFFSET;
        }

        [ContextMenu("Setup")]
        public void Setup() {
            InitializeCombinersAndContainers();

#if UNITY_EDITOR
            Undo.RecordObject(this, "record ceiling");
            SetMeshHoles();
#else
            if (GameManager.House && GameManager.House.HouseBuilder)
                SetMeshHoles();
            else if (IsSetup)
                return;
#endif
            
            var tilesComparer = new Vector3Comparer(Consts.FLOAT_COMPARE_PRECISION);
            if (restrictedPositions == null)
                restrictedPositions = new HashSet<Vector3>(tilesComparer);
            if (forcedPositions == null)
                forcedPositions = new HashSet<Vector3>(tilesComparer);
            if (positionToTile == null)
                positionToTile = new Dictionary<Vector3, CeilingTile>(tilesComparer);

            storeyBuilder = storeyBuilder ?? transform.GetComponentInParent<StoreyBuilder>();
            if (storeyBuilder != null) {
                storeyType = storeyBuilder.storeyType;
            } else {
                Debug.LogError($"[Ceiling] {transform.name}: no storeybuilder was found in parents");
                isSetup = false;
                return;
            }

            var rend = GetComponent<Renderer>();
            var coll = GetComponent<Collider>();

            if (rend == null || coll == null) {
                Debug.LogError($"Ceiling is not setup properly for paneling, renderer or collider is null in {transform.name}");
                isSetup = false;
                return;
            }

            material = rend.sharedMaterial;
            globalYPos = rend.bounds.min.y - TILE_TO_CEILING_Y_OFFSET;

            gameObject.tag = Consts.Tags.CEILING;
            gameObject.layer = LayerMask.NameToLayer(Consts.Layers.CEILING);
            isSetup = true;
        }

#if UNITY_EDITOR
        [HideInPlayMode]
        [Button("Setup")]
        void RefreshSetup() {
            Setup();
        }

        [HideInPlayMode]
        [Button]
        void AddSettingsFromSave() {
            var recording = GetRecording();
            if (recording.modifiedCeilings == null)
                return;
            Undo.RecordObject(this, "loading initial ceiling from save");
            foreach (var setting in recording.modifiedCeilings) {
                if (!initialCeilingModifications.Any(x => x.GetPosition().Equals(setting.GetPosition())) && setting.storey == storeyType)
                    initialCeilingModifications.Add(setting);
            }
        }

        [ContextMenu("Apply missing material")]
        void ApplyMissingMaterial() {
            UnityEditor.Undo.RecordObject(this, "adding missing mat in initial modifications");
            foreach (var initialCeilingModification in initialCeilingModifications) {
                if (initialCeilingModification.materialInstance == null) {
                    initialCeilingModification.materialInstance = materialSlotHelper;
                }
            }
        }

#endif

        public void Setup(Material material) {
            this.material = material;
            Setup();
        }

        public void Setup(StoreyBuilder storeyBuilder) {
            this.storeyBuilder = storeyBuilder;
            Setup();
        }

        public bool ContainsCeilingTile(FloorMapUnit floorMapUnit, out CeilingTile ceilingTile) {
            ceilingTile = null;    
            if (positionToTile.Count == 0)
                return false;

            var floorTilePos = floorMapUnit.floorGameObject.Position;
            var onCeilingPos = new Vector3(floorTilePos.x + Floor.TILE_SIZE, globalYPos, floorTilePos.z);
            
            return positionToTile.TryGetValue(onCeilingPos, out ceilingTile);
        }

        public void ApplySaveData(List<CeilingModification> ceilingModifications, bool logError = true) {
            if(!isSetup)
                Setup();
            try {
                var tiles = new List<CeilingTile>();
                if (!TryGetSaveData(ceilingModifications, out var saveData))
                    return;
                foreach (var ceilingModification in saveData) {
                    var modPos = ceilingModification.GetPosition();
                    var keyPos = new Vector3(modPos.x, globalYPos, modPos.z);
                    if (!positionToTile.ContainsKey(keyPos) && !restrictedPositions.Contains(keyPos)) {
                        InstantiateTileAtPos(keyPos, out var newTile);
                        newTile.SetMaterial(ceilingModification.materialInstance);
                        newTile.SetVisible(true);
                        tiles.Add(newTile);
                    }
                }
                HFEvents.BroadcastOnCeilingChanged(this, tiles);
            } catch (Exception ex) {
                if (logError)
                    Debug.LogError($"[Ceiling] {storeyBuilder.name}: save could not be loaded. {ex.Message}");
            }
        }

        public IEnumerable<CeilingTile> GetTilesBeingModified(Material materialToSkip) {
            foreach (var ceilingTile in currentTiles) {
                if (ceilingTile != null && ceilingTile.Valid && ceilingTile.Renderer.sharedMaterial != materialToSkip)
                    yield return ceilingTile;
            }
        }

        public bool IsRestrictedPosition(Vector3 position) {
#if UNITY_EDITOR
            if (restrictedPositions == null)
                restrictedPositions = new HashSet<Vector3>(new Vector3Comparer(Consts.FLOAT_COMPARE_PRECISION));
#endif
            return restrictedPositions.Contains(position) || (houseBuilderSavingManager.IsHouseBuilderBusy() && IsTilePositionAboveArchitectRooftop(position - architectRooftopOffset));
        }

        public bool IsForcedPosition(Vector3 position) {
#if UNITY_EDITOR
            if (forcedPositions == null)
                forcedPositions = new HashSet<Vector3>(new Vector3Comparer(Consts.FLOAT_COMPARE_PRECISION));
#endif
            return forcedPositions.Contains(position);
        }

        public void AddRestrictedPositions(Vector3[] positions) {
            if (restrictedPositions == null)
                restrictedPositions = new HashSet<Vector3>();
            restrictedPositions.AddRange(positions);
        }
        
        public void AddForcedPositions(Vector3[] positions) {
            if (forcedPositions == null)
                forcedPositions = new HashSet<Vector3>();
            forcedPositions.AddRange(positions);
        }

        public void ModifySelectedTiles(Material material) {
            foreach (var newTile in currentTiles) {
                if (newTile == null)
                    continue;

                newTile.SetMaterial(material);
                newTile.SetVisible(true);
            }

            var modifiedTiles = GetAllModifiedTiles();
            HFEvents.BroadcastOnCeilingChanged(this, modifiedTiles);
            ResetSelectedTiles();
        }

        public void ResetSelectedTiles() {
            currentTiles.Clear();
            readyForTileCollect = true;
        }

        public async void CalculateAndSetTilesBetween(Vector3 firstTilePosition, Vector3 secondTilePosition) {
            if (!readyForTileCollect)
                return;
            
            readyForTileCollect = false;

            float minX;
            float maxX;
            float minZ;
            float maxZ;
            
            if (firstTilePosition.x < secondTilePosition.x) {
                minX = firstTilePosition.x;
                maxX = secondTilePosition.x;
            }
            else {
                maxX = firstTilePosition.x;
                minX = secondTilePosition.x;
            }
            
            if (firstTilePosition.z < secondTilePosition.z) {
                minZ = firstTilePosition.z;
                maxZ = secondTilePosition.z;
            }
            else {
                maxZ = firstTilePosition.z;
                minZ = secondTilePosition.z;
            }

            Room firstRoomTile = null;

            if (TryGetCeilingTileAtPosition(firstTilePosition, firstTilePosition, true, out var tile))
                firstRoomTile = LocationHelper.TryToGetRoomContainingPosition(tile.Center);

            for (var x = minX; x <= maxX; x += Consts.Raycaster.CEILING_TILE_LENGTH) {
                for (var z = minZ; z <= maxZ; z += Consts.Raycaster.CEILING_TILE_LENGTH) {
                    var pos = new Vector3(x, 0, z);
                    if (TryGetCeilingTileAtPosition(pos, pos + CeilingTile.pivotOffset, true, out var ceilingTile) && !currentTiles.Contains(ceilingTile)) {
                        var currentRoom = LocationHelper.TryToGetRoomContainingPosition(ceilingTile.Center);
                        if (AreRoomsDiff(firstRoomTile, currentRoom))
                            continue;
                        currentTiles.Add(ceilingTile);
                        ceilingTile.MarkValid(true);
                        ceilingTile.SetVisible(true);
                    }
                }
            }
        }

        bool AreRoomsDiff(Room firstRoomTile, Room currentRoom) {
            return firstRoomTile == null && currentRoom != null ||
                   firstRoomTile != null && currentRoom == null ||
                   firstRoomTile != null && !firstRoomTile.number.Equals(currentRoom.number);
        }

        public bool TryGetTileAtPosition(Vector3 pos, Vector3 collisionPos, bool physicsCheck, out CeilingTile ceilingTile) {
            return TryGetCeilingTileAtPosition(pos, collisionPos, physicsCheck, out ceilingTile);
        }

        public void SetPreviewTilePosition(Vector3 pos) {
            previewTile.transform.position = pos;
        }

        public void SetTilesSelectableAtPositions(Vector3[] tilesPositions, bool selectable) {
            foreach (var tile in tilesPositions) {
                if (positionToTile.TryGetValue(tile, out CeilingTile ceilingTile))
                    ceilingTile.SetSelectableState(selectable);
            }
        }

        public void RemoveUnusedTiles() {
            foreach (var tmpTile in tmpCreatedTiles) {
                if (tmpTile != null && tmpTile.SavableMaterial == null) {
                    Destroy(tmpTile.gameObject);
                    positionToTile.Remove(tmpTile.GetTileCorner());
                }
            }
        }

        public void DestroyTile(GridCoords gridCoords) {
            foreach (var tile in positionToTile) {
                if (tile.Value.GetGridCoords() == gridCoords) {
                    Destroy(tile.Value.gameObject);
                    positionToTile.Remove(tile.Key);
                    break;
                }
            }
        }

        public void Copy(Ceiling copyFomCeiling) {
            interactionSettings = copyFomCeiling.interactionSettings;
        }

        bool TryGetSaveData(List<CeilingModification> ceilingModifications, out IEnumerable<CeilingModification> data) {
            data = ceilingModifications.Where(x => BelongsTo(x.storey));
            return data.Any();
        }

        void InitializeCombinersAndContainers() {
            tilePrefab = CoreAsset.Singlasset.ceilingTilePrefab;
            SetTilesParent();
            SetCombinersParent();
            SetPreviewTile();

#if UNITY_EDITOR
            tilesCombiner = GetComponent<CeilingTilesCombiner>();
            if (tilesCombiner == null)
                tilesCombiner = Undo.AddComponent<CeilingTilesCombiner>(gameObject);
#else
            tilesCombiner = gameObject.AddOrGetComponent<CeilingTilesCombiner>();
#endif

            tilesCombiner.Initialize(combinerParent, tilesParent, storeyType);
        }

        void SetPreviewTile() {
            const string previewTileName = "PREVIEW TILE";

            var foundPreviewTile = tilesParent.Find(previewTileName);
            if (foundPreviewTile != null) {
                previewTile = foundPreviewTile.GetComponent<CeilingTile>();
            }

            if (previewTile == null) {
                previewTile = Instantiate(tilePrefab, Vector3.zero, tileRotation, tilesParent);
#if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(previewTile.gameObject, "Create preview tile");
#endif
            }

            previewTile.gameObject.name = previewTileName;
            previewTile.SetVisible(false);
        }

        void SetTilesParent() {
            const string tilesParentName = "[Ceiling Tiles]";

            tilesParent = transform.parent.Find(tilesParentName);

            if (tilesParent)
                CopyExistingTiles();
            else 
                tilesParent = new GameObject(tilesParentName).transform;

#if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(tilesParent.gameObject, "Create ceiling tiles container");
#endif
            tilesParent.SetParent(transform.parent);
        }

        public bool TryGetTilesParent(out Transform tilesParentTransform) {
            tilesParentTransform = null;
            if (tilesParent == null)
                return false;
            tilesParentTransform = tilesParent;
            return true;
        }

        void CopyExistingTiles() {
            if (positionToTile == null)
                return;
            foreach (Transform child in tilesParent) {
                var tile = child.GetComponent<CeilingTile>();
                var tileCorner = tile.GetTileCorner();
                if (positionToTile.ContainsKey(tileCorner))
                    continue;
                if (tile != null && !tile.PreviewTile) {
                    positionToTile.Add(tileCorner, tile);
                    tile.OnMaterialChanged += OnTileMaterialChanged;
                }
            }
        }

        void SetCombinersParent() {
            const string ceilingCombinerName = "Ceiling Combiner";

            if (storeyBuilder != null) {
                combinerParent = storeyBuilder.transform.Find(ceilingCombinerName);
            } else if(transform.parent != null) {
                combinerParent = transform.parent.Find(ceilingCombinerName);
            }

            if (combinerParent == null) {
                combinerParent = new GameObject(ceilingCombinerName).transform;
#if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(combinerParent.gameObject, "Create ceiling tiles combiners container");
#endif
                combinerParent.SetParent(transform.parent);
            }
        }

        void SetMeshHoles() {
            if (restrictedPositions == null || restrictedPositions.Count == 0) {
                var tilesComparer = new Vector3Comparer(Consts.FLOAT_COMPARE_PRECISION);
                restrictedPositions = new HashSet<Vector3>(tilesComparer);
                var ceilingHoles = FindObjectsOfType<CeilingMeshHole>();
                if (forcedPositions == null || forcedPositions.Count == 0)
                    forcedPositions = new HashSet<Vector3>(tilesComparer);
                foreach (var hole in ceilingHoles) {
                    if (hole.Mode == CeilingMeshHole.CeilingMeshMode.CutCeiling)
                        restrictedPositions.AddRange(hole.GetHole());
                    else if (hole.Mode == CeilingMeshHole.CeilingMeshMode.ForceCeiling && hole.OnStorey == storeyType)
                        forcedPositions.AddRange(hole.GetHole());
                }
            }
        }

        public void RebuildCeiling(House house) {
            var storey = house.GetStoreyBuilder(storeyType);
            if (storey != null && storey.HasNextFloor()) {
                var subcombinerObject = GetComponent<SubcombinerObject>();
                if (subcombinerObject != null) 
                    CeilingBuilder.BuildCeiling(this, subcombinerObject, material);
            }
            HFEvents.HouseInit -= RebuildCeiling;
        }

        bool BelongsTo(StoreyBuilder.StoreyType storey) {
            return storeyBuilder.storeyType.Equals(storey);
        }

        public StoreyBuilder.StoreyType GetStorey() {
            return storeyType;
        }
        
        bool TryGetCeilingTileAtPosition(Vector3 newTilePos, Vector3 collisionPos, bool physicsCheck, out CeilingTile ceilingTile) {
            ceilingTile = null;

            newTilePos.y = globalYPos;
            collisionPos.y = globalYPos - GET_CEILING_TILE_MARGIN;

            if (positionToTile.TryGetValue(newTilePos, out ceilingTile))
                return true;
            
            if (physicsCheck && Physics.RaycastNonAlloc(collisionPos, Vector3.up, results, GET_CEILING_TILE_CHECK_MAX_DIST, GameManager.Instance.player.GetRaycaster().AllowedCeilingTileLayer) < 1 || 
                IsTilePositionRestricted(newTilePos))
                return false;

            InstantiateTileAtPos(newTilePos, out ceilingTile);
            tmpCreatedTiles.Add(ceilingTile);
            return true;
        }

        void OnTileMaterialChanged(CeilingTile ceilingTile) {
            HFEvents.BroadcastOnCeilingTileChanged(this, ceilingTile);
        }

        IEnumerable<CeilingTile> GetAllModifiedTiles() {
            foreach (var currentTile in currentTiles) {
                if (currentTile != null && !currentTile.PreviewTile)
                    yield return currentTile;
            }
        }

        void InstantiateTileAtPos(Vector3 position, out CeilingTile tile) {
            tile = Instantiate(tilePrefab, position, tileRotation, tilesParent);
            tile.Initialize();
            tile.SetVisible(false);
            positionToTile.Add(position, tile);
            tile.OnMaterialChanged += OnTileMaterialChanged;
        }

        void HandleToolChanged(ToolType obj) {
            if (obj != ToolType.Paint)
                RemoveUnusedTiles();
        }

        void HandleFreshStart() {
            ApplySaveData(initialCeilingModifications, false);
        }

        [Button]
        public void RebuildCeilingTiles() {
            tilesCombiner.RebuildCeilingTiles(tilesParent);
        }

        bool IsTilePositionRestricted(Vector3 position) {
            var restrictedPositionToCheck = new Vector3(position.x, (float) position.y.ToStoreyType(), position.z);
            var isRestrictedByRooftop = GameManager.House.HouseBuilder && GameManager.House.HouseBuilder.IsBusy && IsTilePositionAboveArchitectRooftop(restrictedPositionToCheck);
            return restrictedPositions != null && (restrictedPositions.Contains(restrictedPositionToCheck) || isRestrictedByRooftop);
        }
        
        bool IsTilePositionAboveArchitectRooftop(Vector3 position) {
            var gridCoordsToCheck = HFGrid.TileBLCornerToGridCoords(position);
            if (!houseBuilderSavingManager.ArchitectFloorsDictionary.ContainsKey(gridCoordsToCheck))
                return false;
            var architectRooftopAbove = houseBuilderSavingManager.ArchitectFloorsDictionary[gridCoordsToCheck];
            return architectRooftopAbove != null && architectRooftopAbove.isAtRooftop;
        }

        public void ToggleCeilingCollider(bool toggle) {
            var ceilingCollider = GetComponent<Collider>();
            if (ceilingCollider != null)
                ceilingCollider.enabled = toggle;
        }

        [Serializable]
        struct InteractionSettings {
            public bool blockStairsPlacement;
            public bool blockTilingAndPainting;
        }
    }
}
