using System;
using System.Collections.Generic;
using System.Linq;
using Code.GameManagers;
using ExtensionMethods;
using JetBrains.Annotations;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Features.Pets.Items {
    public class BulbCablesGenerator : SceneSystemSavingHelper {
        const int MAX_POINTS_LIGHTS = 5;
        const float CABLE_THICKNESS = 0.0125F;
        const int MIN_TENSION = 8;
        const int MAX_TENSION = 24;
        const float CABLE_CURVE_RADIUS_FACTOR = 0.1F;
        const float COMPLEXITY_CHANGE_THRESHOLD = 0.25F;
        
        
        static BulbCablesGenerator _instance;
        public static BulbCablesGenerator Instance {
            get {
                if (_instance == null)
                    _instance = FindObjectOfType<BulbCablesGenerator>();
                return _instance;
            }
        }

        public BulbParameters BulbParameters => parameters;
        public Dictionary<Bulb, Vector3> BulbsToPosition { get; } = new Dictionary<Bulb, Vector3>();
        
        [HideInInspector] public Bulb LastBulb;
        [SerializeField] BulbParameters parameters;

        Dictionary<int, List<Bulb>> CurveToBulbs { get; } = new Dictionary<int, List<Bulb>>();
        Dictionary<int, List<Bulb>> CableToBulbs { get; } = new Dictionary<int, List<Bulb>>();
        readonly Stack<CableCurveMesh> cablesCurvesPool = new Stack<CableCurveMesh>();
        readonly Stack<Bulb> bulbsOnCablePool = new Stack<Bulb>();
        readonly Stack<Bulb> bulbsPool = new Stack<Bulb>();
        readonly List<Vector3> curvePoints = new List<Vector3>();
        readonly List<GameObject> cableParents = new List<GameObject>();
        [SerializeField] List<BulbCabledSetting> initialBulbSettings;
        List<BulbCabledSetting> allBulbsSettings;
        List<BulbSettings> lastBulbSettings = new List<BulbSettings>();

        CableCurveMesh[] cables = new CableCurveMesh[0];
        BulbSet.BulbCabledType currentBulbType;
        Transform houseParent;
        Transform currentCableParent;
        int lastBulbIndex = 1;
        int currentCableIndex;
        BulbInteractable currentBulbInteractable;
        bool newCablePendingSave;

        void Awake() {
            houseParent = GameManager.House.transform;
            HFEvents.HouseFreshStart += HandleFreshStart;
        }

        void HandleFreshStart() {
            Load(initialBulbSettings);
        }

        float GetCableThickness()
        {
            return currentBulbType == BulbSet.BulbCabledType.WashingLine ? CABLE_THICKNESS / 4f : CABLE_THICKNESS;
        }
        
#if UNITY_EDITOR
        [HideInPlayMode]
        [Button]
        void AddSettingsFromSave() {
            var recording = GetRecording();
            if (recording.bulbSettings == null)
                return;
            UnityEditor.Undo.RecordObject(this, "loading initial bulbs from save");
            foreach (var setting in recording.bulbSettings) {
                var newSetting = new BulbCabledSetting(setting.settings, setting.turnedOn, false, setting.price, setting.basePrice, setting.seed);
                if(initialBulbSettings.Any(x=> x.settings.Any(y=>y.BulbPositionEqualsTo(new Vector3(newSetting.settings[0].xPos, newSetting.settings[0].yPos, newSetting.settings[0].zPos)))))
                    continue;
                initialBulbSettings.Add(newSetting);
            }
        }
#endif

        void OnDestroy() {
            foreach (var cable in cables) {
                if(cable == null)
                    continue;
                if(cable.mesh != null)
                    Destroy(cable.mesh);
                if(cable.go != null)
                    Destroy(cable.go);
            }
            foreach (var parent in cableParents) {
                if(parent != null)
                    Destroy(parent);
            }

            foreach (var bulb in bulbsPool) {
                if(bulb != null && bulb.gameObject != null)
                    Destroy(bulb.gameObject);
            }

            foreach (var bulb in bulbsOnCablePool) {
                if(bulb != null && bulb.gameObject != null)
                    Destroy(bulb.gameObject);
            }

            BroadcastSettingsToRecording();
            HFEvents.HouseFreshStart -= HandleFreshStart;
        }

        public void SetupBulbType(BulbSet.BulbCabledType type) {
            currentBulbType = type;
            parameters.SetCurrentBulbSet(type);

        }

        public bool TryGetBulbSet(BulbSet.BulbCabledType type, [CanBeNull] out BulbSet set) {
            return parameters.TryGetBulbSet(type, out set);
        }

        public void Reset() {
            if(allBulbsSettings == null)
                allBulbsSettings = new List<BulbCabledSetting>();
            curvePoints.Clear();
            CableToBulbs.Clear();
            BulbsToPosition.Clear();
            LastBulb = null;
        }

        public void ResetLastSettingsAndGenerateNewParent() {
            lastBulbSettings.Clear();
            currentCableParent = GetNewCableParent();
        }

        public void Save() {
            if(newCablePendingSave) {
                if(lastBulbSettings.Count > 1) {
                    var price = GetVisibleBulbExpenses();
                    var basePrice = GetBasePrice();
                    allBulbsSettings.Add(new BulbCabledSetting(lastBulbSettings, true, price, basePrice, currentBulbInteractable.Seed));
                    SetupInteractable(currentBulbInteractable.IsOn, price, basePrice);
                    SaveTurnedOn(currentBulbInteractable);
                    newCablePendingSave = false;
                }
                else if (CableToBulbs.ContainsKey(currentCableIndex)){
                    foreach (var bulb in CableToBulbs[currentCableIndex]) {
                        HandleBulbRemoved(bulb, false);
                    }
                    CableToBulbs.Remove(currentCableIndex);
                    Destroy(currentCableParent.gameObject);
                }
            }
            BroadcastSettingsToRecording();
        }

        void BroadcastSettingsToRecording() {
            if(allBulbsSettings != null)
                HFEvents.BroadcastBulbsCabledModified(this, allBulbsSettings.Where(x => !x.notSavable).ToList());
        }

        void SetupInteractable(bool turnedOn, float price, float basePrice) {
            if(CableToBulbs.ContainsKey(currentCableIndex)) {
                foreach(var bulb in CableToBulbs[currentCableIndex]) {
                    currentBulbInteractable.AddRendererToHighlight(bulb.Renderer);
                    currentBulbInteractable.SetTurnedOn(turnedOn);
                }
                
                currentBulbInteractable.SetPrice(price, basePrice);
            }
        }

        public void Load(List<BulbCabledSetting> settings) {
            if (settings == null)
                return;

            foreach (var settingList in settings) {
                Reset();
                if(settingList.settings.Count > 0)
                    currentBulbType = settingList.settings[0].type;
                ResetLastSettingsAndGenerateNewParent();
                for (int j = 0; j < settingList.settings.Count - 1; j++) {
                    SetupBulbType(settingList.settings[j].type);
                    Vector3 aPos = new Vector3(settingList.settings[j].xPos, settingList.settings[j].yPos, settingList.settings[j].zPos);
                    Vector3 aDir = new Vector3(settingList.settings[j].xDir, settingList.settings[j].yDir, settingList.settings[j].zDir);
                    Vector3 bPos = new Vector3(settingList.settings[j + 1].xPos, settingList.settings[j + 1].yPos,
                        settingList.settings[j + 1].zPos);
                    Vector3 bDir = new Vector3(settingList.settings[j + 1].xDir, settingList.settings[j + 1].yDir,
                        settingList.settings[j + 1].zDir);
                    if(j < 1)
                        PlaceHookBulb(aPos, aDir, 0, settingList.settings[j].price);
                    GenerateCable(new KeyValuePair<Vector3, Vector3>(aPos, aDir),
                        new KeyValuePair<Vector3, Vector3>(bPos, bDir), settingList.settings[j + 1].tension, settingList.settings[j].price, true, 0, true);
                }

                lastBulbSettings = new List<BulbSettings>(settingList.settings);
                SetupInteractable(settingList.turnedOn, settingList.price, settingList.basePrice);
                allBulbsSettings.Add(settingList);
            }
            newCablePendingSave = false;
            
            HFEvents.BroadcastBulbChainModified(allBulbsSettings, null);
        }

        public void RemoveSetting(int index) {
            var bulbCabledSetting = allBulbsSettings[index];
            bulbCabledSetting.notSavable = true;
            allBulbsSettings[index] = bulbCabledSetting;
            BroadcastSettingsToRecording();
            lastBulbSettings.Clear();
            
            HFEvents.BroadcastBulbChainModified(allBulbsSettings, lastBulbSettings);
        }

        public void SaveTurnedOn(BulbInteractable interactable) {
            if(interactable.CableIndex > allBulbsSettings.Count - 1)
                return;
            var bulbCabledSetting = allBulbsSettings[interactable.CableIndex];
            bulbCabledSetting.turnedOn = interactable.IsOn;
            allBulbsSettings[interactable.CableIndex] = bulbCabledSetting;
            BroadcastSettingsToRecording();
        }

        Transform GetNewCableParent() {
            currentCableIndex = cableParents.Count;
            var parent = new GameObject("BulbCabled" + cableParents.Count).transform;
            parent.SetParent(houseParent);
            cableParents.Add(parent.gameObject);
            currentBulbInteractable = parent.gameObject.AddComponent<BulbInteractable>();
            currentBulbInteractable.Setup(this, allBulbsSettings.Count, currentBulbType);
            newCablePendingSave = true;
            return parent;
        }

        public void HideLastCable() {
            var c = cables[cables.Length - 1];
            cablesCurvesPool.Push(c);
            c.go.transform.SetParent(houseParent);
            c.SetEnabled(false);
            Array.Resize(ref cables, cables.Length - 1);
            HideBulbsOnLastCable();
            lastBulbIndex--;
        }

        void HideBulbsOnLastCable() {
            foreach(var bulb in CurveToBulbs[lastBulbIndex])  {
                bulbsOnCablePool.Push(bulb);
                bulb.transform.SetParent(houseParent);
                bulb.SetVisible(false);
                bulb.SetPlaced(false);
                CableToBulbs[currentCableIndex].Remove(bulb);
                OverlapCache.TryRemoveInteractableCacheElement(bulb.Collider);
            }

            CurveToBulbs.Remove(lastBulbIndex);
        }

        public void GenerateCable(KeyValuePair<Vector3, Vector3> a, KeyValuePair<Vector3, Vector3> b, float tension, float price, bool generatingNewCable = false, int index = 0, bool saveLoad = false) {
            curvePoints.Clear();
            Vector3 cableDownLocalDir = (a.Value + b.Value).normalized;
            Vector3 cableLeftLocalDir = Vector3.Cross((b.Key - a.Key).normalized, cableDownLocalDir).normalized;
            float distanceBetweenPoints = (a.Key - b.Key).magnitude;

            var deltaY = Mathf.Clamp(Mathf.Abs((a.Key - b.Key).y), 0f, 1f);
            var invTension = tension > 0 ? 1f / tension : 1f;
            var complexity = Mathf.Lerp(MAX_TENSION, MIN_TENSION, deltaY < COMPLEXITY_CHANGE_THRESHOLD ? invTension : deltaY * invTension);
            
            int cablePoints = Mathf.FloorToInt(distanceBetweenPoints * complexity);
            float step = 1f / cablePoints;
            float t = 0f;
            for (int i = 0; i <= cablePoints; i++) {
                var point0 = GetPointOnBezier(a, b, tension, t);

                curvePoints.Add(point0);
                t += step;
            }

            var cableIndex = generatingNewCable ? lastBulbIndex + 1 : index;
            
            var newCable = GetOrMakeCable(parameters.GetProperMaterial(currentBulbType), GetCableThickness(), cableIndex);
            newCable.CreateMesh(curvePoints, cableLeftLocalDir, cableDownLocalDir * 1.25f);

            UpdateCableSettings();
            
            if(generatingNewCable) {
                PlaceNewBulb(b.Key, b.Value, tension, price, out var previousBulb);
                SpawnBulbsBetween(LastBulb, previousBulb);
            }
            else {
                SpawnBulbsBetween();
            }

            if(generatingNewCable)
                LightBulbsEvenly();

            if(!saveLoad) {
                HFEvents.BroadcastBulbChainModified(allBulbsSettings, lastBulbSettings);
            }

            void SpawnBulbsBetween(Bulb firstBulbInChain = null, Bulb lastBulbInChain = null)
            {
                var distance = (a.Key - b.Key).magnitude;
                int number = Mathf.CeilToInt(distance * parameters.GetBulbsOnCableDensity());
                
                float bulbStep = 1f / number;
                bool shouldLerpToUpRot = Vector3.Dot(a.Value.normalized, b.Value.normalized) > 0.98f;
                float bulbUpDirLerpValue = shouldLerpToUpRot ? parameters.GetBulbsUpDirectionLerpValue() : 0.01f;
                
                var bulbInd = 0;
                if (CurveToBulbs.TryGetValue(cableIndex, out var bulbs)) {
                    for (float i = bulbStep; i < 1f - bulbStep/2.5f; i += bulbStep) {
                        var bulbPosition = GetPointOnBezier(a, b, tension, i);
                        var bulb = bulbs[bulbInd];
                        bulb.SetPosRot(bulbPosition, a.Value, !shouldLerpToUpRot, bulbUpDirLerpValue, bulbInd, firstBulbInChain, lastBulbInChain);
                        bulb.SetupInteractable(currentBulbInteractable);
                        bulbInd++;
                    }
                    return;
                }
                bulbInd = 0;
                var bulbsOnCable = new List<Bulb>();
                for (float i = bulbStep; i < 1f - bulbStep/2.5f; i += bulbStep) {
                    var bulbPosition = GetPointOnBezier(a, b, tension, i);
                    var bulb = GetNewCableBulb();
                    if(bulb == null)
                        continue;

                    bulb.SetPrice(price);
                    bulb.SetPosRot(bulbPosition, a.Value, !shouldLerpToUpRot, bulbUpDirLerpValue, bulbInd, firstBulbInChain, lastBulbInChain);
                    bulb.SetPlaced(true);
                    bulbsOnCable.Add(bulb);
                    bulb.SetupInteractable(currentBulbInteractable);
                    bulb.LetThereBeLight(currentBulbInteractable != null && currentBulbInteractable.IsOn);
                    if(generatingNewCable)
                        bulb.SetupChainBulbs(firstBulbInChain, lastBulbInChain);
                    AddBulbToDict(bulb);
                    bulbInd++;
                }
                CurveToBulbs.Add(cableIndex, bulbsOnCable);
            }


            void UpdateCableSettings() {
                if(!TryToSearchSettingAndModify())
                    ModifyLastSetting();

                bool TryToSearchSettingAndModify() {
                    for (int i = 0; i < allBulbsSettings.Count; i++) {
                        for(int j = 0; j < allBulbsSettings[i].settings.Count; j++) {
                            if (allBulbsSettings[i].settings[j].BulbPositionEqualsTo(b.Key)) {
                                var modifiedSetting = allBulbsSettings[i].settings[j];
                                modifiedSetting.tension = tension;
                                allBulbsSettings[i].settings[j] = modifiedSetting;
                                return true;
                            }
                        }
                    }
                    return false;
                }

                void ModifyLastSetting() {
                    TryAddOrModifySetting(a);
                    TryAddOrModifySetting(b);

                    void TryAddOrModifySetting(KeyValuePair<Vector3, Vector3> bulbSettings) {
                        for(int i = 0; i < lastBulbSettings.Count; i++) {
                            if(lastBulbSettings[i].BulbPositionEqualsTo(bulbSettings.Key)) {
                                var modifiedSetting = lastBulbSettings[i];
                                modifiedSetting.tension = tension;
                                lastBulbSettings[i] = modifiedSetting;
                                return;
                            }
                        }
                        lastBulbSettings.Add(new BulbSettings(bulbSettings.Key, bulbSettings.Value, tension, lastBulbIndex, price, currentBulbType));
                    }
                }
            }
        }

        public float GetAllExpenses(float singleBulbPrice, float distance) {
            return GetVisibleBulbExpenses() + GetNextCableExpenses(singleBulbPrice, distance);
        }

        float GetNextCableExpenses(float singleBulbPrice, float distance) {
            int bulbToPlaceOnCable = Mathf.FloorToInt(distance * parameters.GetBulbsOnCableDensity());
            return singleBulbPrice * (bulbToPlaceOnCable + 1);
        }

        public float GetVisibleBulbExpenses() {
            float expenses = 0f;
            if(CableToBulbs.ContainsKey(currentCableIndex)) {
                foreach(var bulb in CableToBulbs[currentCableIndex]) {
                    expenses += bulb.Price;
                }
            }
            return expenses;
        }

        float GetBasePrice() {
            if(CableToBulbs.ContainsKey(currentCableIndex)) {
                return CableToBulbs[currentCableIndex][0].Price;
            }
            return 0;
        }

        void LightBulbsEvenly() {
            var allCount = BulbsToPosition.Count;
            if (MAX_POINTS_LIGHTS >= allCount) {
                for (int i = 0; i < allCount; i++) {
                    BulbsToPosition.ElementAt(i).Key.SetAsConsideredASourceOfLight(true);
                }
            }
            else {
                for (int i = 0; i < allCount; i++) {
                    BulbsToPosition.ElementAt(i).Key.SetAsConsideredASourceOfLight(false);
                }

                for (int i = 0; i < MAX_POINTS_LIGHTS; i++) {
                    float j = Mathf.Round((float) ((double) i / (MAX_POINTS_LIGHTS - 1) * (allCount - 1)));
                    BulbsToPosition.ElementAt((int) j).Key.SetAsConsideredASourceOfLight(true);
                }
            }

            currentBulbInteractable.RefreshLights();
        }

        Vector3 GetPointOnBezier(KeyValuePair<Vector3, Vector3> a, KeyValuePair<Vector3, Vector3> b, float tension,
            float delta) {
            return Bezier.GetPosition(a.Key,
                Vector3.Lerp(a.Key + a.Value * CABLE_CURVE_RADIUS_FACTOR,
                    b.Key + b.Value * CABLE_CURVE_RADIUS_FACTOR + Vector3.down * tension / 10f, 1f / 3f),
                Vector3.Lerp(a.Key + a.Value * CABLE_CURVE_RADIUS_FACTOR,
                    b.Key + b.Value * CABLE_CURVE_RADIUS_FACTOR + Vector3.down * tension / 10f, 2f / 3f), b.Key, delta);
        }


        void PlaceNewBulb(Vector3 pos, Vector3 forward, float tension, float price, out Bulb previousBulb) {
            previousBulb = LastBulb;
            PlaceHookBulb(pos, forward, tension, price);
        }
        public void PlaceHookBulb(Vector3 pos, Vector3 contactNormal, float tension, float price) {
            var newBulb = GetNewBulb();
            if(newBulb == null)
                return;

            newBulb.Setup(pos, contactNormal, parameters.GetBulbsUpDirectionLerpValue(), tension, lastBulbIndex, LastBulb, price, currentBulbInteractable);
            newBulb.SetupChainBulbs(newBulb, LastBulb);
            LastBulb = newBulb;
            BulbsToPosition.Add(LastBulb, pos);
            LastBulb.SetPlaced(true);

            AddBulbToDict(LastBulb);

            currentBulbInteractable.AssignLights(CableToBulbs[currentCableIndex]);
        }

        public void HandleBulbRemoved(Bulb bulb, bool modifyCollection = true) {
            bulbsPool.Push(bulb);
            bulb.transform.SetParent(houseParent);
            OverlapCache.TryRemoveInteractableCacheElement(bulb.Collider);
            bulb.SetVisible(false);
            bulb.LetThereBeLight(false);
            currentBulbInteractable.RemoveRendererToHighlight(bulb.Renderer);
            BulbsToPosition.Remove(bulb);
            LightBulbsEvenly();
            var bulbNumber = lastBulbSettings.Count;
            if (bulbNumber != 0) 
                lastBulbSettings.RemoveAt(bulbNumber - 1);
            if (modifyCollection && CableToBulbs.ContainsKey(currentCableIndex) && CableToBulbs[currentCableIndex].Contains(bulb))
                CableToBulbs[currentCableIndex].Remove(bulb);
            HFEvents.BroadcastBulbChainModified(allBulbsSettings, lastBulbSettings);
        }

        void AddBulbToDict(Bulb bulb) {
            if (CableToBulbs.ContainsKey(currentCableIndex)) {
                CableToBulbs[currentCableIndex].Add(bulb);
            }
            else {
                CableToBulbs.Add(currentCableIndex, new List<Bulb>{bulb});
            }
        }

        [CanBeNull]
        Bulb GetNewBulb() {
            TryGetBulbSet(currentBulbType, out var set);
            bool isInPool = bulbsPool.Count > 0 && bulbsPool.ElementAt(0).IsType(currentBulbType);

            var newBulb = isInPool ? bulbsPool.Pop() : set != null ? Instantiate(set.bulbPrefab) : null;
            if (newBulb == null)
                return null;

            newBulb.transform.SetParent(currentCableParent);
            newBulb.SetVisible(true);
            newBulb.LetThereBeLight(currentBulbInteractable != null && currentBulbInteractable.IsOn);
            return newBulb;
        }

        [CanBeNull]
        Bulb GetNewCableBulb() {
            TryGetBulbSet(currentBulbType, out var set);
            bool isInPool = bulbsOnCablePool.Count > 0 && bulbsOnCablePool.ElementAt(0).IsType(currentBulbType);
            var newBulb = isInPool
                ? bulbsOnCablePool.Pop()
                : set != null ? Instantiate(set.bulbOnCablePrefab) : null;

            if (newBulb == null)
                return null;

            newBulb.transform.SetParent(currentCableParent);
            newBulb.SetVisible(true);
            newBulb.LetThereBeLight(currentBulbInteractable != null && currentBulbInteractable.IsOn);
            return newBulb;
        }

        CableCurveMesh GetOrMakeCable(Material mat, float thickness, int cableInd) {
            foreach (var cable in cables) {
                if (cable.HasIndex(cableInd)) {
                    cable.Setup(mat, cable.parent, thickness, cableInd);
                    return cable;
                }
            }

            lastBulbIndex = cableInd;
            Array.Resize(ref cables, cables.Length + 1);
            if (cablesCurvesPool.Count > 0) {
                var cable = cablesCurvesPool.Pop();
                cable.Setup(mat, currentCableParent, thickness, lastBulbIndex);
                cables[cables.Length - 1] = cable;
                return cable;
            }

            var newCable = new CableCurveMesh();
            cables[cables.Length - 1] = newCable;
            newCable.Setup(mat, currentCableParent, thickness, lastBulbIndex, true);
            return newCable;
        }
    }

    [Serializable]
    public struct BulbCabledSetting {
        public List<BulbSettings> settings;
        public bool notSavable;
        public bool turnedOn;
        public bool wasCreatedByPlayer;
        public float price;
        public float basePrice;
        public int seed;

        public BulbCabledSetting(List<BulbSettings> settings, bool wasCreatedByPlayer, float price, float basePrice, int seed) {
            this.settings = new List<BulbSettings>();
            foreach(var setting in settings) {
                this.settings.Add(new BulbSettings(setting));
            }
            notSavable = false;
            turnedOn = false;
            this.price = price;
            this.basePrice = basePrice;
            this.wasCreatedByPlayer = wasCreatedByPlayer;
            this.seed = seed;
        }
        public BulbCabledSetting(List<BulbSettings> settings, bool turnedOn, bool wasCreatedByPlayer, float price, float basePrice, int seed) {
            this.settings = new List<BulbSettings>();
            foreach(var setting in settings) {
                this.settings.Add(new BulbSettings(setting));
            }
            notSavable = false;
            this.price = price;
            this.basePrice = basePrice;
            this.wasCreatedByPlayer = wasCreatedByPlayer;
            this.turnedOn = turnedOn;
            this.seed = seed;
        }
    }

    [Serializable]
    public class BulbSet {
        public Bulb bulbPrefab;
        public Bulb bulbOnCablePrefab;
        public BulbCabledType type;
        public int bulbOnChainDensity = 5;
        [Range(0.01f, 1.0f)] public float bulbsOnChainUpDirLerpValue = 0.3f;

        public enum BulbCabledType {
            RegularBulbs,
            Lanterns,
			RegularWhite,
			RegularYellow,
			RegularOrange,
			RegularRed,
			RegularBlue,
			RegularBlueDark,
			RegularGreen,
			RegularPurple,
			RegularPink,
            Pumpkin1,
            Pumpkin2,
            Pumpkin3,
            Pumpkin4,
            PaperLanternSmall,
            PaperLanternBig,
            WashingLine
        }
    }
}