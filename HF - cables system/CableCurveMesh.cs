using System.Collections.Generic;
using UnityEngine;


namespace Features.Pets.Items {
    [System.Serializable]
    public class CableCurveMesh {
        public Mesh mesh;
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public GameObject go;
        public Transform parent;
        List<Vector3> verts;
        List<Vector2> uvs;
        List<int> indices;
        float thickness;
        int index;

        public void Setup(Material mat, Transform parent, float thickness, int index, bool firstSetup = false) {
            if(firstSetup) {
                go = new GameObject {hideFlags = HideFlags.DontSave};
                mesh = new Mesh();
                meshFilter = go.AddComponent<MeshFilter>();
                meshFilter.mesh = mesh;
                meshRenderer = go.AddComponent<MeshRenderer>();

                verts = new List<Vector3>();
                uvs = new List<Vector2>();
                indices = new List<int>();
                this.thickness = thickness;
                mesh.hideFlags = HideFlags.DontSave;
                meshRenderer.sharedMaterial = mat;
                go.tag = Consts.Tags.COMBINER;
            }

            this.parent = parent;
            go.name = $"Cable {index}";
            go.transform.SetParent(parent);
            mesh.name = go.name + " mesh";
            this.index = index;
        }

        public bool HasIndex(int ind) {
            return index == ind;
        }

        public void CreateMesh(IReadOnlyList<Vector3> curvePoints, Vector3 leftLocalDir, Vector3 downLocalDir) {
            verts.Clear();
            uvs.Clear();
            indices.Clear();

            int currentIndex = 0;

            for (int i = 1; i < curvePoints.Count; i++) {
                verts.Add(curvePoints[i - 1]);
                verts.Add(curvePoints[i - 1] + leftLocalDir * thickness);
                verts.Add(curvePoints[i]);
                verts.Add(curvePoints[i] + leftLocalDir * thickness);
                AddUvsAndIndices(-downLocalDir);
                currentIndex += 4;

                verts.Add(curvePoints[i - 1] + downLocalDir * thickness);
                verts.Add(curvePoints[i - 1]);
                verts.Add(curvePoints[i] + downLocalDir * thickness);
                verts.Add(curvePoints[i]);
                AddUvsAndIndices(-leftLocalDir);
                currentIndex += 4;

                verts.Add(curvePoints[i - 1] + downLocalDir * thickness + leftLocalDir * thickness);
                verts.Add(curvePoints[i - 1] + downLocalDir * thickness);
                verts.Add(curvePoints[i] + downLocalDir * thickness + leftLocalDir * thickness);
                verts.Add(curvePoints[i] + downLocalDir * thickness);
                AddUvsAndIndices(downLocalDir);
                currentIndex += 4;

                verts.Add(curvePoints[i - 1] + leftLocalDir * thickness);
                verts.Add(curvePoints[i - 1] + downLocalDir * thickness + leftLocalDir * thickness);
                verts.Add(curvePoints[i] + leftLocalDir * thickness);
                verts.Add(curvePoints[i] + downLocalDir * thickness + leftLocalDir * thickness);
                AddUvsAndIndices(leftLocalDir);
                currentIndex += 4;
            }


            SetEnabled(true);
            Build();

            void AddUvsAndIndices(Vector3 normal) {
                uvs.Add(normal);
                uvs.Add(normal);
                uvs.Add(normal);
                uvs.Add(normal);
                indices.Add(currentIndex + 0);
                indices.Add(currentIndex + 3);
                indices.Add(currentIndex + 1);

                indices.Add(currentIndex + 0);
                indices.Add(currentIndex + 2);
                indices.Add(currentIndex + 3);
            }

            void Build() {
                mesh.Clear();
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
            }
        }

        public void SetEnabled(bool enabled) {
            meshRenderer.enabled = enabled;
        }
    }
}