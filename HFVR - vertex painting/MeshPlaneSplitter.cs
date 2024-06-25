using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace _Code.VertexPainting{
    
    public static class MeshPlaneSplitter {
        
        public static void Execute(MeshPlaneSplitterArgs args, out List<PlaneManagement> subWalls){
            var vertices = new List<Vector3>();
            if(Application.isPlaying)
                args.meshFilter.mesh.GetVertices(vertices);
            else
                args.meshFilter.sharedMesh.GetVertices(vertices);
            TranslateVerticesToWorldPosition(args, out var vertexList);
            SplitMeshIntoPlaneGroups(vertexList, args, out var planeGroups);
            CreateVertexArrays(args, planeGroups, out subWalls, vertices.Count);
        }
        
        static void SplitMeshIntoPlaneGroups(IReadOnlyList<Vertex> vertices, MeshPlaneSplitterArgs args, out List<PlaneGroup> planeGroups) {
            planeGroups = new List<PlaneGroup>();
            for (int i = 0; i < vertices.Count; i++){
                var vertex = vertices[i];
                var haveFoundGroup = false;
                for (int n = 0; n < planeGroups.Count; n++) {
                    if (!planeGroups[n].TryToAppendGroupIfBelongsTo(vertex)) 
                        continue;
                    haveFoundGroup = true;
                }

                if (!haveFoundGroup) {
                    planeGroups.Add(new PlaneGroup(new List<Vertex>{vertex}, args));
                }
            }
            planeGroups = planeGroups.Where(x => x.vertices[0].normal != Vector3.up && x.vertices[0].normal != Vector3.down).ToList();
        }

        static void CreateVertexArrays(MeshPlaneSplitterArgs args, IReadOnlyList<PlaneGroup> planeGroups, out List<PlaneManagement> subWalls, int verticesLength){
            subWalls = new List<PlaneManagement>();
            for (int i = 0; i < planeGroups.Count; i++){
                //subWalls.Add(planeManagement);
                subWalls.AddRange(TryToSplitMore(args, planeGroups[i], verticesLength));
            }

            foreach (var subWall in subWalls){
                subWall.management.FillTheGaps();
            }
        }

        static IEnumerable<PlaneManagement> TryToSplitMore(MeshPlaneSplitterArgs args, PlaneGroup planeGroup, int verticesLength) {
            var planeManagement = new PlaneManagement(args.wallConfiguration, GetVertexArrayFromVertices(planeGroup.vertices), verticesLength, planeGroup.positionValue);
            var firstRow = planeManagement.management.parameters.Vertices2DArray[0];
            for (int j = 1; j < firstRow.Length; j++) {
                if (firstRow[j].index >= 0 || firstRow[j - 1].index <= -1)
                    continue;
                int n;
                var found = false;
                for (n = j + 1; n < firstRow.Length; n++) {
                    if (firstRow[n].index > -1) {
                        found = true;
                        break;
                    }
                }

                var leftSurface = new List<Vertex>();
                var rightSurface = new List<Vertex>();
                foreach (var vertex in planeManagement.management.parameters.Vertices2DArray) {
                    leftSurface.AddRange(vertex.Take(j).ToList());
                    var secondSplitIndex = found ? n : j;
                    rightSurface.AddRange(vertex.Skip(secondSplitIndex).ToList());
                }

                return new[] {
                    new PlaneManagement(args.wallConfiguration, GetVertexArrayFromVertices(leftSurface), verticesLength, planeGroup.positionValue),
                    new PlaneManagement(args.wallConfiguration, GetVertexArrayFromVertices(rightSurface), verticesLength, planeGroup.positionValue)
                };
            }

            return new[] {planeManagement};
        }

        static Vertex[][] GetVertexArrayFromVertices(IReadOnlyList<Vertex> vertices){
            var vertexPlane = new Vertex[0][];
            for (int i = 0; i < vertices.Count; i++){
                var vertex = vertices[i];
                var vertex2DArrayLength = vertexPlane.Length;
                if (vertex2DArrayLength < 1){
                    ResizeAndAppend2DArray(vertex, ref vertexPlane);
                    continue;
                }
                if (AddVertexToExistingRowIfExists(vertex2DArrayLength, vertex, ref vertexPlane))
                    continue;
                ResizeAndAppend2DArray(vertex, ref vertexPlane);
            }
            return vertexPlane;
        }

        static void ResizeAndAppend2DArray(Vertex vertex, ref Vertex[][] vertexArray){
            Array.Resize(ref vertexArray, vertexArray.Length + 1);
            vertexArray[vertexArray.Length - 1] = new[]{vertex};
        }

        static bool AddVertexToExistingRowIfExists(int vertex2DArrayLength, Vertex vertex, ref Vertex[][] vertexArray){
            for (int j = 0; j < vertex2DArrayLength; j++){
                if (vertexArray[j].Any(x => x.worldPosition.y.ApproximatelyTheSame(vertex.worldPosition.y, 0.05f))){
                    vertexArray[j] = vertexArray[j].Append(vertex).ToArray();
                    return true;
                }
            }
            return false;
        }
        public static void TranslateVerticesToWorldPosition(MeshPlaneSplitterArgs args, out List<Vertex> vertices){
            vertices = new List<Vertex>();
            var mesh = args.meshFilter.sharedMesh;
            var meshVertices = new List<Vector3>();
            var meshNormals = new List<Vector3>();
            mesh.GetVertices(meshVertices);
            mesh.GetNormals(meshNormals);

            for (int i = 0; i < meshVertices.Count; i++){
                vertices.Add(new Vertex{
                    worldPosition = args.wallTransform.TransformPoint(meshVertices[i]).RoundToFloatingPoint(),
                    normal = args.wallTransform.TransformDirection(meshNormals[i]).Round(),
                    index = i
                });
            }
        }
    }
    
    

    [Serializable]
    public class PlaneGroup{
        public List<Vertex> vertices;
        public float positionValue;
        [SerializeField] Vector3 normal;

        public PlaneGroup(List<Vertex> vertices, MeshPlaneSplitterArgs args){
            this.vertices = vertices;
            positionValue = (float)Math.Round(CalculatePositionValue(vertices, args, out normal), 4);
        }

        public bool TryToAppendGroupIfBelongsTo(Vertex vertex) {
            if (DoesBelongToTheGroup(vertex)) {
                vertices.Add(vertex);
                return true;
            }
            return false;
        }

        bool DoesBelongToTheGroup(Vertex vertex){
            if (normal == Vector3.left || normal == Vector3.right)
                return normal == vertex.normal && CloseToPositionValue(vertex.worldPosition.x);

            if (normal == Vector3.forward || normal == Vector3.back)
                return normal == vertex.normal && CloseToPositionValue(vertex.worldPosition.z);
            return false;
        }

        bool CloseToPositionValue(float number){
            return positionValue.ApproximatelyTheSame((float)Math.Round(number, 4), 0.1f);
        }

        float CalculatePositionValue(IReadOnlyList<Vertex> vertices, MeshPlaneSplitterArgs args, out Vector3 vertexNormal){
            var vertex = vertices[0];
            vertexNormal = vertex.normal.Round();
            normal = vertexNormal;
            if (vertexNormal == Vector3.left || vertexNormal == Vector3.right){
                return vertex.worldPosition.x;
            }
            if (vertexNormal == Vector3.forward || vertexNormal == Vector3.back)
                return vertex.worldPosition.z;

            Debug.LogError("[MeshPlaneSplitter]: incorrect normals");
            return vertex.worldPosition.y;
        }
    }

    public struct PlaneManagement{
        public readonly WallVerticesManagement management;

        public PlaneManagement(VertexPaintingConfiguration config, Vertex[][] vertices, int verticesCount, float positionValue) : this(){
            management = GetVerticesManagementByWallOrientation(config, vertices, verticesCount, positionValue);
        }
        static WallVerticesManagement GetVerticesManagementByWallOrientation(VertexPaintingConfiguration config, Vertex[][] vertices, int verticesCount, float positionValue){
            config.EvaluateWallAxis(vertices[0][0].normal, out var wallOrientation);
            var args = new WallVerticesManagementArgs{vertices2dArray = vertices, verticesCount = verticesCount, orientation = wallOrientation, positionValue = positionValue};
            switch (wallOrientation){
                default:
                case Wall.WallOrientation.X:
                    return new XAxisVerticesManagement(args);
                case Wall.WallOrientation.Z:
                    return new ZAxisVerticesManagement(args);
                case Wall.WallOrientation.XNegative:
                    return new XNegativeVerticesManagement(args);
                case Wall.WallOrientation.ZNegative:
                    return new ZNegativeVerticesManagement(args);
            }
        }
    }

    public struct MeshPlaneSplitterArgs{
        public MeshFilter meshFilter;
        public Transform wallTransform;
        public VertexPaintingConfiguration wallConfiguration;
    }
}
