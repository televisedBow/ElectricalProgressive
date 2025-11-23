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
        private Dictionary<BlockPos, MeshRef> wireMeshRefs = new Dictionary<BlockPos, MeshRef>();
        private Matrixf modelMat = new Matrixf();

        public ImmersiveWireRenderer(ICoreClientAPI api)
        {
            capi = api;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Opaque) return;
            if (wireMeshRefs.Count == 0) return;

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlEnableCullFace();
            rpi.GLEnableDepthTest();

            IStandardShaderProgram prog = rpi.PreparedStandardShader(
                (int)camPos.X, (int)camPos.Y, (int)camPos.Z);



            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;

            foreach (var kvp in wireMeshRefs)
            {
                BlockPos pos = kvp.Key;
                MeshRef meshRef = kvp.Value;

                if (meshRef != null)
                {
                    prog.ModelMatrix = modelMat
                        .Identity()
                        .Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z)
                        .Values;

                    //AssetLocation wiretexture = new AssetLocation(capi.World.GetBlock(con.BlockId).Attributes["texture"].ToString());
                    AssetLocation wiretexture = new AssetLocation("game:block/metal/plate/copper.png");
                
                    int textureid = rpi.GetOrLoadTexture(wiretexture);
                    rpi.BindTexture2d(textureid);

                    rpi.RenderMesh(meshRef);
                }
            }

            prog.Stop();


        }



        public void UpdateWireMesh(BlockPos pos, MeshData mesh)
        {
            // Удаляем старый меш если существует
            if (wireMeshRefs.TryGetValue(pos, out MeshRef oldMeshRef))
            {
                oldMeshRef?.Dispose();
            }

            if (mesh != null && mesh.VerticesCount > 0)
            {
                // Загружаем новый меш в GPU
                MeshRef newMeshRef = capi.Render.UploadMesh(mesh);
                wireMeshRefs[pos] = newMeshRef;
            }
            else
            {
                wireMeshRefs.Remove(pos);
            }
        }

        public void RemoveWireMesh(BlockPos pos)
        {
            if (wireMeshRefs.TryGetValue(pos, out MeshRef meshRef))
            {
                meshRef?.Dispose();
                wireMeshRefs.Remove(pos);
            }
        }

        public void Dispose()
        {
            foreach (var meshRef in wireMeshRefs.Values)
            {
                meshRef?.Dispose();
            }
            wireMeshRefs.Clear();
        }
    }
}