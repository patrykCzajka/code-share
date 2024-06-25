using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace _Code.VertexPainting{
    [Serializable]
    public class WallVerticesManagement{
        public WallManagementCacheParameters parameters;

        public WallVerticesManagement(WallVerticesManagementArgs args){
            parameters.verticesCount = args.verticesCount;
            parameters.Vertices2DArray = args.vertices2dArray;
            parameters.orientation = args.orientation;
            parameters.positionValue = args.positionValue;
            GroupFromUpperLeft();
            FillTheGaps();
            CacheVertexList();
            CacheRanges();
        }

        public WallVerticesManagement(WallManagementCacheParameters copyFrom){
            parameters = copyFrom;
        }

        public void Initialise() {
            parameters.Vertices2DArray = new Vertex[parameters.columnLength][];
            for (int i = 0; i < parameters.columnLength; i++){
                parameters.Vertices2DArray[i] = parameters.serializableVertexRowList[i].rowVertices.ToArray();
            }
        }

        void CacheVertexList(){
            parameters.serializableVertexRowList = new List<Row>();
            for (int i = 0; i < parameters.Vertices2DArray.Length; i++){
                parameters.serializableVertexRowList.Add(new Row(parameters.Vertices2DArray[i].ToList()));
            }
        }

        public virtual void FillTheGaps(){ }

        public Vector3 GetTopLeftCorner(){
            return parameters.Vertices2DArray[0][0].worldPosition;
        }

        public Vector3 GetBottomRightCorner() {
            return parameters.serializableVertexRowList[parameters.columnLength - 1].rowVertices[parameters.rowLength - 1].worldPosition;
        }

        public bool IsCorrectlyCalculated(){
            for (int i = 0; i < parameters.columnLength; i++){
                if (parameters.Vertices2DArray[i].Length != parameters.rowLength)
                    return false;
            }

            return true;
        }

        public IEnumerable<Vertex> GetNeighbours(VerticesToPaintArgs verticesToPaintArgs, Vector3 toolPosition){
            var rowIndex = GetRowIndex(toolPosition);
            var columnIndex = GetColumnIndex(toolPosition);
                if (rowIndex < 0 || columnIndex < 0)
                    yield break;
                if (GetClosestIndex(columnIndex, rowIndex, out var closestVertex))
                    yield return closestVertex;
                for (int i = 0; i <= verticesToPaintArgs.numberOfNeighbours; i++){
                    for (int j = 0; j <= verticesToPaintArgs.numberOfNeighbours; j++){
                        var distanceFromMiddleInUnits = i + j;
                        if (verticesToPaintArgs.modShape == Wall.ModificationShape.Circle && IsTooFarFromClosestVertex(i,j,verticesToPaintArgs.numberOfNeighbours))
                            continue;
                        var alpha = verticesToPaintArgs.config.EvaluateAlphaForRadius(distanceFromMiddleInUnits);
                        if (GetNeighbourByCoordinates(columnIndex + i, rowIndex + j, alpha, out var neighbour))
                            yield return neighbour;

                        if (GetNeighbourByCoordinates(columnIndex + i, rowIndex - j, alpha, out neighbour))
                            yield return neighbour;

                        if (GetNeighbourByCoordinates(columnIndex - i, rowIndex + j, alpha, out neighbour))
                            yield return neighbour;

                        if (GetNeighbourByCoordinates(columnIndex - i, rowIndex - j, alpha, out neighbour))
                            yield return neighbour;
                    }
            }
        }
        public IEnumerable<Vertex> GetNeighboursInRectShape(VerticesToPaintArgs verticesToPaintArgs, Vector3 toolPosition){
            var rowIndex = GetRowIndex(toolPosition);
            var columnIndex = GetColumnIndex(toolPosition);
                if (rowIndex < 0 || columnIndex < 0)
                    yield break;
                if (GetClosestIndex(columnIndex, rowIndex, out var closestVertex))
                    yield return closestVertex;
                for (int i = 0; i <= verticesToPaintArgs.numberOfNeighbours * 2; i++){
                    for (int j = 0; j <= verticesToPaintArgs.numberOfNeighbours; j++){
                        var distanceFromMiddleInUnits = i + j;
                        if (verticesToPaintArgs.modShape == Wall.ModificationShape.Circle && IsTooFarFromClosestVertex(i,j,verticesToPaintArgs.numberOfNeighbours))
                            continue;
                        var alpha = verticesToPaintArgs.config.EvaluateAlphaForRadius(distanceFromMiddleInUnits);
                        if (GetNeighbourByCoordinates(columnIndex + i, rowIndex + j, alpha, out var neighbour))
                            yield return neighbour;

                        if (GetNeighbourByCoordinates(columnIndex + i, rowIndex - j, alpha, out neighbour))
                            yield return neighbour;

                        if (GetNeighbourByCoordinates(columnIndex - i, rowIndex + j, alpha, out neighbour))
                            yield return neighbour;

                        if (GetNeighbourByCoordinates(columnIndex - i, rowIndex - j, alpha, out neighbour))
                            yield return neighbour;
                    }
            }
        }
        protected Vertex[] GetRowGapFilled(List<Vertex> row, float step, Vertex[] firstRow){
            var rowCount = row.Count;
            var news = new Vertex[rowCount];
            row.CopyTo(news);
            
            var rowToFill = news.ToList();
            for (int i = 1; i < rowCount; i++){
                var currentVertex = row[i];
                var previousVertex = row[i - 1];
                var distanceToNextVertex = GetDistanceBetweenVertices(currentVertex, previousVertex);
                if (i == rowCount - 1 && rowCount < firstRow.Length) {
                    distanceToNextVertex = GetDistanceBetweenVertices (currentVertex, firstRow.Last());
                }
                else if (distanceToNextVertex <= step*1.5f) 
                    continue;
                var numberOfMissingVertices = Mathf.Round(distanceToNextVertex / step);
                for (int n = 1; n < numberOfMissingVertices; n++){
                    var filledVertex = new Vertex{worldPosition = GetNextGapVertexPositionBasedOn(previousVertex, step).RoundToFloatingPoint(), index = -1};
                    rowToFill.Add(filledVertex);
                    previousVertex = filledVertex;
                }

            }
            return rowToFill.ToArray();
        }

        protected virtual float GetDistanceBetweenVertices(Vertex first, Vertex second){
            return -1f;
        }

        protected virtual Vector3 GetNextGapVertexPositionBasedOn(Vertex previous, float step){
            return Vector3.zero;
        }

        protected virtual void GroupFromUpperLeft(){
            parameters.Vertices2DArray = parameters.Vertices2DArray.OrderByDescending(x => x[0].worldPosition.y).ToArray();
        }

        static bool IsTooFarFromClosestVertex(int x, int y, int neighbours){
            if (x == 0 || y == 0)
                return x + y >= neighbours;
            return x + y >= neighbours + 1;
        }

        protected virtual int GetRowIndex(Vector3 toolPosition){
            return -1;
        }

        int GetColumnIndex(Vector3 toolPosition){
            var y = toolPosition.y;
            for (int i = 0; i < parameters.columnLength - 1; i++){
                var currentY = parameters.columnRange[i].worldPosition.y;
                if (y > currentY)
                    continue;
                var nextY = parameters.columnRange[i + 1].worldPosition.y;
                if (y < currentY && y > nextY){
                    var mid = (currentY + nextY) / 2f;
                    return y < mid ? i : i + 1;
                }
            }
            return -1;
        }

        bool GetClosestIndex(int x, int y, out Vertex closestVertex){
            var vertexIndex = parameters.Vertices2DArray[x][y].index;
            if (vertexIndex > -1){
                closestVertex = new Vertex{index = vertexIndex};
                return true;
            }
            closestVertex = new Vertex();
            return false;
        }

        bool GetNeighbourByCoordinates(int x, int y, float alpha, out Vertex neighbour){
            neighbour = new Vertex();
            if (x >= 0 && x < parameters.columnLength && y >= 0 && y < parameters.rowLength){
                var vertexIndex = parameters.Vertices2DArray[x][y].index;
                if (vertexIndex < 0)
                    return false;
                neighbour.index = vertexIndex;
                neighbour.alpha = alpha;
                return true;
            }
            return false;
        }

        void CacheRanges(){
            if (parameters.serializableVertexRowList.Count <= 0) {
                Debug.LogError("[WallVerticesManagement]: serializableRowList is null");
                return;
            }

            parameters.rowRange = parameters.serializableVertexRowList[0].rowVertices.ToArray();
            parameters.columnRange = parameters.Vertices2DArray.Select(x => x[0]).ToArray();
            parameters.columnLength = parameters.serializableVertexRowList.Count;
            parameters.rowLength = parameters.serializableVertexRowList[0].rowVertices.Count;
        }
    }

    [Serializable]
    public struct Row{
        public List<Vertex> rowVertices;
        
        public Row(IReadOnlyList<Vertex> vertices) {
            rowVertices = new List<Vertex>();
            for (int i = 0; i < vertices.Count; i++) {
                rowVertices.Add(vertices[i]);
            }
        }
    }

    public struct WallVerticesManagementArgs{
        public Vertex[][] vertices2dArray;
        public int verticesCount;
        public Wall.WallOrientation orientation;
        public float positionValue;
    }

    [Serializable]
    public struct WallManagementCacheParameters{
        public Vertex[][] Vertices2DArray;
        public int verticesCount;
        public List<Row> serializableVertexRowList;
        public int rowLength;
        public Vertex[] rowRange;
        public int columnLength;
        public Vertex[] columnRange;
        public Wall.WallOrientation orientation;
        public int uniqueIndex;
        public float positionValue;
    }
}