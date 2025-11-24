using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace EPImmersive.Utils
{
    public class ImmersiveWireRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 99;

        private ICoreClientAPI capi;

        private Dictionary<BlockPos, List<WireMeshData>> wireMeshData = new Dictionary<BlockPos, List<WireMeshData>>();

        private Matrixf modelMat = new Matrixf();

        public ImmersiveWireRenderer(ICoreClientAPI api)
        {
            capi = api;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Opaque) return;
            if (wireMeshData.Count == 0) return;

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlEnableCullFace();
            rpi.GLEnableDepthTest();

            IStandardShaderProgram prog = rpi.PreparedStandardShader(
                (int)camPos.X, (int)camPos.Y, (int)camPos.Z);

            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;

            foreach (var kvp in wireMeshData)
            {
                BlockPos pos = kvp.Key;
                List<WireMeshData> meshDataList = kvp.Value;

                foreach (var meshData in meshDataList)
                {
                    if (meshData.MeshRef != null)
                    {
                        prog.ModelMatrix = modelMat
                            .Identity()
                            .Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z)
                            .Values;

                        // Используем текстуру соответствующего материала
                        AssetLocation wiretexture = new AssetLocation($"game:block/metal/plate/{meshData.Material}.png");
                        int textureid = rpi.GetOrLoadTexture(wiretexture);
                        rpi.BindTexture2d(textureid);

                        rpi.RenderMesh(meshData.MeshRef);
                    }
                }
            }

            prog.Stop();
        }

        public void RemoveWireMesh(BlockPos pos)
        {
            if (wireMeshData.TryGetValue(pos, out List<WireMeshData> meshDataList))
            {
                foreach (var meshData in meshDataList)
                {
                    meshData.MeshRef?.Dispose();
                }
                wireMeshData.Remove(pos);
            }
        }





        public void UpdateWireMesh(BlockPos pos, List<WireMeshData> meshDataList)
        {
            // Удаляем старые меши если существуют
            if (wireMeshData.TryGetValue(pos, out List<WireMeshData> oldMeshData))
            {
                foreach (var meshData in oldMeshData)
                {
                    meshData.MeshRef?.Dispose();
                }
            }

            if (meshDataList != null && meshDataList.Count > 0)
            {
                // Загружаем новые меши в GPU
                var newMeshData = new List<WireMeshData>();
                foreach (var meshData in meshDataList)
                {
                    if (meshData.Mesh != null && meshData.Mesh.VerticesCount > 0)
                    {
                        MeshRef newMeshRef = capi.Render.UploadMesh(meshData.Mesh);
                        newMeshData.Add(new WireMeshData
                        {
                            MeshRef = newMeshRef,
                            Material = meshData.Material
                        });
                    }
                }
                wireMeshData[pos] = newMeshData;
            }
            else
            {
                wireMeshData.Remove(pos);
            }
        }





        public void Dispose()
        {
            foreach (var meshRef in wireMeshData.Values)
            {
                foreach (var data in meshRef)
                {
                    data.Mesh?.Dispose();
                    data.MeshRef?.Dispose();
                }
               
            }
            wireMeshData.Clear();
        }


        // Структура для хранения меша с материалом
        public struct WireMeshData
        {
            public MeshRef MeshRef;
            public string Material;
            public MeshData Mesh; // Для временного хранения при создании
        }
    }
}