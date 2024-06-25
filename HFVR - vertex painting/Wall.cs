using System;
using System.Collections.Generic;
using System.Linq;
using _Code.Systems;
using UnityEngine;
using Object = UnityEngine.Object;

namespace _Code.VertexPainting{
#if UNITY_EDITOR
    [UnityEditor.CanEditMultipleObjects]
#endif
    [Serializable]
    public class Wall : MonoBehaviour {
        const float VERTEX_COLOR_APPROXIMATION_VALUE = 0.1f;
        
        public WallVerticesCache CacheAsset;
        public string InstanceID { get => instanceId; private set => instanceId = value; }
        public IEnumerable<Vertex> verticesToPaint;
        public List<SubWall> SubWalls;
        
        [SerializeField] string instanceId;
        [SerializeField] bool drawGizmos;
        [SerializeField] VertexPaintingConfiguration configuration;
        [SerializeField] PaintingConfiguration colorsConfig;
        [SerializeField] MeshFilter meshFilter;
        readonly List<Color> colors = new List<Color>();
        SkillSystem skillSystem;


        #region Initialisation
        public void Initialise(){
            SubWalls.Clear();
            for (int i = 0; i < CacheAsset.SubWallsParameters.Count; i++){
                var cachedSubWall = CacheAsset.SubWallsParameters[i];
                if(cachedSubWall.orientation == WallOrientation.X)
                    SubWalls.Add(new SubWall(this,new XAxisVerticesManagement(cachedSubWall)));
                
                else if(cachedSubWall.orientation == WallOrientation.XNegative)
                    SubWalls.Add(new SubWall(this,new XNegativeVerticesManagement(cachedSubWall)));
                
                else if(cachedSubWall.orientation == WallOrientation.Z)
                    SubWalls.Add(new SubWall(this,new ZAxisVerticesManagement(cachedSubWall)));
                
                else if(cachedSubWall.orientation == WallOrientation.ZNegative)
                    SubWalls.Add(new SubWall(this,new ZNegativeVerticesManagement(cachedSubWall)));
                SubWalls[i].management.Initialise();
            }
        }

        public void AssignSkillSystem(SkillSystem skillSystem) {
            this.skillSystem = skillSystem;
        }
        
        #endregion

        #region Collect refs
        
        public void AssignMeshFilter(){
            meshFilter = GetComponent<MeshFilter>();
        }

        public void SetVertexConfiguration(VertexPaintingConfiguration config){
            configuration = config;
        }
        
        #endregion

        public void Color(ColoringArgs coloringArgs, out bool inRange){
            if (coloringArgs.toolDistance > configuration.GetMaxDistance(coloringArgs.modificationType) && !coloringArgs.paintCanExplosion) {
                inRange = false;
                return;
            }
            inRange = true;
            meshFilter.sharedMesh.GetColors(colors);
            var verticesToPaintArgs = GetVerticesToPaintArgs(coloringArgs);
            verticesToPaint = GetVerticesToPaint(verticesToPaintArgs, coloringArgs.toolPosition);
            SetVertexColors(verticesToPaint, coloringArgs, verticesToPaintArgs);
        }
        static IEnumerable<Vertex> GetVerticesToPaint(VerticesToPaintArgs verticesToPaintArgs, Vector3 toolPosition){
            if (verticesToPaintArgs.modShape == ModificationShape.Rectangle) {
                foreach (var vertex in verticesToPaintArgs.management.GetNeighboursInRectShape(verticesToPaintArgs, toolPosition))
                    yield return vertex;
                yield break;
            }
            foreach (var vertex in verticesToPaintArgs.management.GetNeighbours(verticesToPaintArgs, toolPosition))
                yield return vertex;
        }

        VerticesToPaintArgs GetVerticesToPaintArgs(ColoringArgs args){
            var brushSize = skillSystem.HasSkill(args.skill) ? configuration.EvaluateBrushSize(args.modificationType, args.toolDistance)*2 : configuration.EvaluateBrushSize(args.modificationType, args.toolDistance);
            var verticesToPaintArgs = new VerticesToPaintArgs{
                management = args.subWall.management,
                config = configuration,
                modShape = configuration.GetModificationShape(args.modificationType),
                colorLerpWeight = args.modificationType == ModificationType.Brush ? configuration.EvaluateColorLerpWeight(args.toolDistance, skillSystem.HasSkill(SkillType.FasterPainting)) : 1f,
                numberOfNeighbours = brushSize
            };
            if(args.modificationType == ModificationType.Hammer) {
                verticesToPaintArgs.numberOfNeighbours *= args.toolPosition.y > 1.5f || args.hammerThrow ? 2 : 1;
                if(HQ.GameSettings.seatedMode)
                    verticesToPaintArgs.numberOfNeighbours *= 2;
            }

            return verticesToPaintArgs;
        }

        void SetVertexColors(IEnumerable<Vertex> verticesToColor, ColoringArgs colorArgs, VerticesToPaintArgs args){
            var defaultColor = GetColorOfVertexWithIndex(colorArgs);
            while(colors.Count < colorArgs.subWall.management.parameters.verticesCount)
                colors.Add(defaultColor);
            foreach (var neighbour in verticesToColor) {
                var currentColor = colors[neighbour.index];
                var targetColor = GetColorOfVertexWithIndex(colorArgs, neighbour.index);
                if (colorArgs.paintCanExplosion) {
                    colors[neighbour.index] = UnityEngine.Color.Lerp(currentColor,  targetColor, 1f);
                }
                else if (colorArgs.modificationType == ModificationType.Hammer || currentColor.ApproximatelyTheSame(targetColor, VERTEX_COLOR_APPROXIMATION_VALUE))
                    colors[neighbour.index] = targetColor;
                else
                    colors[neighbour.index] = UnityEngine.Color.LerpUnclamped(currentColor, targetColor, args.colorLerpWeight * neighbour.alpha);
            }

            meshFilter.sharedMesh.SetColors(colors);
        }

        Color GetColorOfVertexWithIndex(ColoringArgs colorArgs, int vertexIndex = 0) {
            switch (colorArgs.modificationType){
                default:
                case ModificationType.Brush:
                    return new Color(colorArgs.color.r, colorArgs.color.g, colorArgs.color.b, colors[vertexIndex].a);
                case ModificationType.Spatula:
                case ModificationType.Hammer:
                    return configuration.GetTint(colorArgs.modificationType);
            }
        }

        public List<Color> GetAllColors() {
            var colors = new List<Color>();
            meshFilter.sharedMesh.GetColors(colors);
            return colors;
        }

        public List<Color> GetSaveData(){
            var colors = new List<Color>();
            meshFilter.mesh.GetColors(colors);
            return colors;
        }

        public void LoadSaveData(List<Color> colorMatrix){
            var colors = new List<Color>();
            meshFilter.mesh.GetColors(colors);
            while(colors.Count < colorMatrix.Count)
                colors.Add(new Color());
            meshFilter.mesh.SetColors(colorMatrix);
        }


        #region Editor
        Color GetVertexColorInEditor(ColoringArgs colorArgs, int vertexIndex = 0, int columnIndex = 0) {
            var color = colorArgs.color;
            if (colorArgs.subWall.selectiveVertexModification) {
                foreach (var subWallColoring in colorArgs.subWall.subWallColoring) {
                    if (columnIndex >= subWallColoring.startColumnNumber && columnIndex < subWallColoring.endColumnNumber) {
                        color = colorsConfig.GetPaletteColor(subWallColoring.color);
                        break;
                    }
                }
            }
            switch (colorArgs.modificationType){
                default:
                case ModificationType.Brush:
                    return new Color(color.r, color.g, color.b, colors[vertexIndex].a);
                case ModificationType.Spatula:
                case ModificationType.Hammer:
                    return configuration.GetTint(colorArgs.modificationType);
            }
        }
        
        [ContextMenu("Print mesh readability")]
        void PrintMeshReadability() {
            Debug.Log(meshFilter.sharedMesh.isReadable);
        }
        public void InitialiseColorMatrix(){
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "");
#endif
            var args = new ColoringArgs{color = configuration.GetTint(ModificationType.Hammer), modificationType = ModificationType.Hammer};
            MeshPlaneSplitter.TranslateVerticesToWorldPosition(new MeshPlaneSplitterArgs{wallTransform = transform, meshFilter = meshFilter}, out _);
            var mesh = meshFilter.mesh;
            if (!mesh.isReadable) {
                Debug.LogError("[Wall]: Please enable Write/Read in mesh!!!");
                return;
            }
            mesh.GetColors(colors);
            var defaultColor = GetColorOfVertexWithIndex(args);
            
            while(colors.Count < mesh.vertices.Length)
                colors.Add(defaultColor);
            for (int i = 0; i < colors.Count; i++){
                colors[i] = GetColorOfVertexWithIndex(args);
            }
#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCompleteObjectUndo(mesh, "wall color matrix");
            UnityEditor.EditorUtility.SetDirty(mesh);
#endif
            mesh.SetColors(colors);
        }

#if UNITY_EDITOR
        void OnDrawGizmos(){
            if (!drawGizmos || SubWalls == null)
                return;
            for (int i = 0; i < SubWalls.Count; i++){
                var subWall = SubWalls[i];
                UnityEditor.Handles.color = UnityEngine.Color.gray;
                var parameters = subWall.management.parameters;
                if(parameters.serializableVertexRowList.Count <= 0)
                    break;
                var verticesRow = parameters.serializableVertexRowList[parameters.serializableVertexRowList.Count/2];
                var middleVertexPos = verticesRow.rowVertices[verticesRow.rowVertices.Count/2].worldPosition;
                UnityEditor.Handles.Label(new Vector3(middleVertexPos.x, 0.25f, middleVertexPos.z), $"{parameters.orientation.ToString()}\n{i.ToString()}");
                if (subWall.showVertices){
                    foreach (var row in subWall.management.parameters.Vertices2DArray){
                        foreach (var vertex in row){
                            UnityEditor.Handles.color = UnityEngine.Color.white;
                            UnityEditor.Handles.Label(vertex.worldPosition, vertex.worldPosition.ToString());
                            Gizmos.DrawSphere(vertex.worldPosition, 0.0075f);
                            Gizmos.color = UnityEngine.Color.blue;
                            Gizmos.DrawRay(vertex.worldPosition, vertex.normal*0.1f);
                        }
                    }
                }
            }
        }

#endif
        public void Cache(){
            InstanceID = gameObject.GetInstanceID().ToString();
            var meshPlaneSplitterArgs = new MeshPlaneSplitterArgs{
                wallTransform = transform,
                meshFilter = meshFilter,
                wallConfiguration = configuration
            };
            MeshPlaneSplitter.Execute(meshPlaneSplitterArgs, out var planeMatrices);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(CacheAsset);
#endif
            CacheAsset.SubWallsParameters = new List<WallManagementCacheParameters>();
            foreach (var planeMatrix in planeMatrices)
                CacheAsset.SubWallsParameters.Add(planeMatrix.management.parameters);
            CacheAsset.SubWallsParameters = CacheAsset.SubWallsParameters.OrderBy(x => x.uniqueIndex).ToList();
            
#if UNITY_EDITOR
            Initialise();
#endif
        }

        public void SaveColors(){
            var mesh = meshFilter.sharedMesh;
            mesh.GetColors(colors);
            foreach (var subWall in SubWalls){
                if(!subWall.applyColor)
                    continue;
                var args = new ColoringArgs{subWall = subWall, color = colorsConfig.GetPaletteColor(subWall.color), modificationType = subWall.modType};
                foreach (var row in subWall.management.parameters.serializableVertexRowList){
                    while(colors.Count < args.subWall.management.parameters.verticesCount)
                        colors.Add(GetColorOfVertexWithIndex(args));
                    for (int i = 0; i < row.rowVertices.Count; i++) {
                        if(row.rowVertices[i].index < 0)
                            continue;
                        colors[row.rowVertices[i].index] = GetVertexColorInEditor(args, row.rowVertices[i].index, i);
                    }

                    meshFilter.sharedMesh.SetColors(colors);
                }
            }
        }

        [SerializeField] int[] wallsToColor;

        [SerializeField] PaintingConfiguration.PaletteColor color;

        [SerializeField] ModificationType type;

        public void SaveMultipleColors() {
            var mesh = meshFilter.sharedMesh;
            mesh.GetColors(colors);
            for(int n = 0; n < SubWalls.Count; n++) {
                if(!wallsToColor.Contains(n))
                    continue;
                var subWall = SubWalls[n];
                var args = new ColoringArgs{subWall = subWall, color = colorsConfig.GetPaletteColor(color), modificationType = type};
                foreach (var row in subWall.management.parameters.serializableVertexRowList){
                    while(colors.Count < args.subWall.management.parameters.verticesCount)
                        colors.Add(GetColorOfVertexWithIndex(args));
                    for (int i = 0; i < row.rowVertices.Count; i++) {
                        if(row.rowVertices[i].index < 0)
                            continue;
                        colors[row.rowVertices[i].index] = GetVertexColorInEditor(args, row.rowVertices[i].index, i);
                    }

                    meshFilter.sharedMesh.SetColors(colors);
                }
            }
        }

        public void ClearData() {
            CacheAsset.SubWallsParameters = new List<WallManagementCacheParameters>();
            SubWalls = new List<SubWall>();
        }

        [ContextMenu("Remove corrupted data")]
        void RemoveCorruptedData() {
            CacheAsset.SubWallsParameters = CacheAsset.SubWallsParameters.Where(x => x.rowLength < 2 && x.orientation == WallOrientation.XNegative && x.positionValue.ApproximatelyTheSame(0.5f, 0.1f)).ToList();
            SubWalls = SubWalls.Where(x => x.management.parameters.rowLength < 2 && x.management.parameters.orientation == WallOrientation.XNegative && x.management.parameters.positionValue.ApproximatelyTheSame(0.5f, 0.1f)).ToList();
            
        }

        #endregion

        #region Enums

        public enum WallOrientation{
            X = 0,
            XNegative = 1,
            Z = 2,
            ZNegative = 3
        }

        public enum ModificationShape{
            Square,
            Circle,
            Rectangle
        }

        public enum ModificationType{
            Brush,
            Spatula,
            Hammer
        }
#endregion

    }

    [Serializable]
    public class SubWall{
        [SerializeField] public Wall wall;
        [SerializeField] public WallVerticesManagement management;
        public bool showVertices;
        public PaintingConfiguration.PaletteColor color = PaintingConfiguration.PaletteColor.White;
        public Wall.ModificationType modType = Wall.ModificationType.Brush;
        public bool selectiveVertexModification;
        public List<SubWallColoring> subWallColoring;
        public bool applyColor;

        public SubWall(Wall wall, WallVerticesManagement management){
            this.wall = wall;
            this.management = management;
        }
    }

    [Serializable]
    public class SubWallColoring {
        public int startColumnNumber;
        public int endColumnNumber;
        public PaintingConfiguration.PaletteColor color;
    }

    [Serializable]
    public class Vertex{
        public Vector3 worldPosition;
        public Vector3 normal;
        public int index;
        public float alpha;
    }

    public struct VerticesToPaintArgs{
        public WallVerticesManagement management;
        public Wall.ModificationShape modShape;
        public VertexPaintingConfiguration config;
        public int numberOfNeighbours;
        public float colorLerpWeight;
    }
    
    public struct ColoringArgs{
        public SubWall subWall;
        public Color color;
        public Wall.ModificationType modificationType;
        public Vector3 toolPosition;
        public float toolDistance;
        public bool paintCanExplosion;
        public bool hammerThrow;
        public SkillType skill;
    }
}