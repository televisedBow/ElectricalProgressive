using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace EPImmersive.Utils
{
    public class WireMesh
    {
        /// <summary>
        /// Builds a wire mesh from pos1 to pos2 with configurable sag.
        /// </summary>
        /// <param name="pos1">First anchor point (world coordinates)</param>
        /// <param name="pos2">Second anchor point (world coordinates)</param>
        /// <param name="thickness">Wire thickness (default 0.015)</param>
        /// <param name="sagFactor">
        /// Controls how much the wire sags.
        /// 0.0f  – completely straight (no sag)
        /// 0.1f  – light sag
        /// 0.166f–0.2f – realistic power-line sag (default ~0.166f)
        /// 0.5f  – very strong sag
        /// >1.0f – extreme sag (can be used for decorative ropes, etc.)
        /// </param>
        /// <returns>Generated MeshData</returns>
        static public MeshData MakeWireMesh(Vec3f pos1, Vec3f pos2, float thickness = 0.015f, float rel_sag= 0.05f)
        {

            float t = thickness;
            Vec3f dPos = pos2 - pos1;
            float dist = pos2.DistanceTo(pos1);

            // Количество секций провода (всегда четное)
            int nSec = (int)Math.Floor(dist * 2);
            if (nSec % 2 == 1) nSec++;
            nSec = nSec > 6 ? nSec : 6;

            MeshData mesh = new MeshData(4, 6);
            mesh.SetMode(EnumDrawMode.Triangles);

            MeshData mesh_top = new MeshData(4, 6);
            mesh_top.SetMode(EnumDrawMode.Triangles);

            MeshData mesh_bot = new MeshData(4, 6);
            mesh_bot.SetMode(EnumDrawMode.Triangles);

            MeshData mesh_side = new MeshData(4, 6);
            mesh_side.SetMode(EnumDrawMode.Triangles);

            MeshData mesh_side2 = new MeshData(4, 6);
            mesh_side2.SetMode(EnumDrawMode.Triangles);

            //out of plane translation vector:
            Vec3f P1 = new Vec3f(-dPos.Z, 0, dPos.X);
            float blen = P1.Length();
            if (blen < 1e-5f)
            {
                P1 = new Vec3f(1, 0, 0);
            }
            else
            {
                P1.Normalize();
            }
            Vec3f DD = dPos.Clone().Normalize();
            Vec3f P2 = DD.Cross(P1);
            P2.Normalize();
            P2.Negate();

            Vec3f pos;

            mesh_top.Flags.Fill(0);
            mesh_bot.Flags.Fill(0);
            mesh_side.Flags.Fill(0);
            mesh_side2.Flags.Fill(0);

            //Add vertices
            for (int j = 0; j <= nSec; j++)
            {
                float x = dPos.X / nSec * j;
                float y = dPos.Y / nSec * j;
                float z = dPos.Z / nSec * j;
                float l = (float)Math.Sqrt(x * x + y * y + z * z);

                float hdist = (float)Math.Sqrt(dPos.X * dPos.X + dPos.Z * dPos.Z);
                float dy;
                if (hdist < 1e-5f)
                {
                    dy = 0f;
                }
                else
                {
                    float a = hdist / (8f * rel_sag);
                    float h_progress = (float)j / nSec * hdist;
                    dy = a * ((float)Math.Cosh((h_progress - hdist / 2f) / a) - (float)Math.Cosh(hdist / 2f / a));
                }
                pos = new Vec3f(x, y + dy, z);

                float du = dist / 2 / t / nSec;
                int color = 1;
                mesh_top.AddVertex(pos1.X + pos.X + P2.X * t - P1.X * t, pos1.Y + pos.Y + P2.Y * t - P1.Y * t, pos1.Z + pos.Z + P2.Z * t - P1.Z * t, j * du, 0, color);
                mesh_top.AddVertex(pos1.X + pos.X + P2.X * t + P1.X * t, pos1.Y + pos.Y + P2.Y * t + P1.Y * t, pos1.Z + pos.Z + P2.Z * t + P1.Z * t, j * du, 1, color);

                mesh_bot.AddVertex(pos1.X + pos.X - P2.X * t - P1.X * t, pos1.Y + pos.Y - P2.Y * t - P1.Y * t, pos1.Z + pos.Z - P2.Z * t - P1.Z * t, j * du, 0, color);
                mesh_bot.AddVertex(pos1.X + pos.X - P2.X * t + P1.X * t, pos1.Y + pos.Y - P2.Y * t + P1.Y * t, pos1.Z + pos.Z - P2.Z * t + P1.Z * t, j * du, 1, color);

                mesh_side.AddVertex(pos1.X + pos.X - P1.X * t + P2.X * t, pos1.Y + pos.Y - P1.Y * t + P2.Y * t, pos1.Z + pos.Z - P1.Z * t + P2.Z * t, j * du, 1, color);
                mesh_side.AddVertex(pos1.X + pos.X - P1.X * t - P2.X * t, pos1.Y + pos.Y - P1.Y * t - P2.Y * t, pos1.Z + pos.Z - P1.Z * t - P2.Z * t, j * du, 0, color);

                mesh_side2.AddVertex(pos1.X + pos.X + P1.X * t + P2.X * t, pos1.Y + pos.Y + P1.Y * t + P2.Y * t, pos1.Z + pos.Z + P1.Z * t + P2.Z * t, j * du, 1, color);
                mesh_side2.AddVertex(pos1.X + pos.X + P1.X * t - P2.X * t, pos1.Y + pos.Y + P1.Y * t - P2.Y * t, pos1.Z + pos.Z + P1.Z * t - P2.Z * t, j * du, 0, color);

                mesh_top.Flags[2 * j] = VertexFlags.PackNormal(P2.X, P2.Y, P2.Z);
                mesh_top.Flags[2 * j + 1] = VertexFlags.PackNormal(P2.X, P2.Y, P2.Z);

                mesh_bot.Flags[2 * j] = VertexFlags.PackNormal(-P2.X, -P2.Y, -P2.Z);
                mesh_bot.Flags[2 * j + 1] = VertexFlags.PackNormal(-P2.X, -P2.Y, -P2.Z);

                mesh_side.Flags[2 * j] = VertexFlags.PackNormal(-P1.X, -P1.Y, -P1.Z);
                mesh_side.Flags[2 * j + 1] = VertexFlags.PackNormal(-P1.X, -P1.Y, -P1.Z);

                mesh_side2.Flags[2 * j] = VertexFlags.PackNormal(P1.X, P1.Y, P1.Z);
                mesh_side2.Flags[2 * j + 1] = VertexFlags.PackNormal(P1.X, P1.Y, P1.Z);

            }

            //add indices
            for (int j = 0; j < nSec; j++)
            {
                //upper stripe
                int offset = 2 * j;
                mesh_top.AddIndex(offset);
                mesh_top.AddIndex(offset + 3);
                mesh_top.AddIndex(offset + 2);
                mesh_top.AddIndex(offset);
                mesh_top.AddIndex(offset + 1);
                mesh_top.AddIndex(offset + 3);

                //lower stripe
                mesh_bot.AddIndex(offset);
                mesh_bot.AddIndex(offset + 3);
                mesh_bot.AddIndex(offset + 1);
                mesh_bot.AddIndex(offset);
                mesh_bot.AddIndex(offset + 2);
                mesh_bot.AddIndex(offset + 3);

                //sides 
                mesh_side.AddIndex(offset);
                mesh_side.AddIndex(offset + 3);
                mesh_side.AddIndex(offset + 1);
                mesh_side.AddIndex(offset);
                mesh_side.AddIndex(offset + 2);
                mesh_side.AddIndex(offset + 3);

                mesh_side2.AddIndex(offset);
                mesh_side2.AddIndex(offset + 3);
                mesh_side2.AddIndex(offset + 2);
                mesh_side2.AddIndex(offset);
                mesh_side2.AddIndex(offset + 1);
                mesh_side2.AddIndex(offset + 3);

            }

            mesh.AddMeshData(mesh_top);
            mesh.AddMeshData(mesh_bot);
            mesh.AddMeshData(mesh_side);
            mesh.AddMeshData(mesh_side2);
            mesh.Rgba.Fill((byte)255);

            return mesh;
        }
    }
}