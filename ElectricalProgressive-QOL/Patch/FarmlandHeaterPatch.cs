using ElectricalProgressive.Content.Block.EHeater;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Patch;

public class FarmlandHeaterPatch
{
    private static MethodInfo heaterBonusMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonus");
    private static HarmonyMethod transpilerMethod;

    /// <summary>
    /// Метод для регистрации патча
    /// </summary>
    /// <param name="harmony"></param>
    /// <exception cref="Exception"></exception>
    public static void RegisterPatch(Harmony harmony)
    {
        // Ищем private метод Update с параметром float
        var originalMethod = typeof(BlockEntityFarmland).GetMethod("Update",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (originalMethod == null)
        {
            throw new Exception("Could not find BlockEntityFarmland.Update method!");
        }

        transpilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic));

        harmony.Patch(originalMethod, transpiler: transpilerMethod);
    }

    /// <summary>
    /// Метод для отмены патча
    /// </summary>
    /// <param name="harmony"></param>
    public static void UnregisterPatch(Harmony harmony)
    {
        var originalMethod = typeof(BlockEntityFarmland).GetMethod("Update",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (originalMethod != null)
        {
            harmony.Unpatch(originalMethod, transpilerMethod.method);
        }
    }





    /// <summary>
    /// Транспайлер для вставки вызова HeaterBonus в метод Update грядки
    /// </summary>
    /// <param name="instructions"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var found = false;
        for (int i = 0; i < codes.Count - 2; i++)
        {
            // Ищем последовательность: roomness > 0 -> baseClimate.Temperature += 5f
            if (codes[i].opcode == OpCodes.Ldarg_0 &&
                codes[i + 1].opcode == OpCodes.Ldfld &&
                (codes[i + 1].operand as FieldInfo)?.Name == "roomness" &&
                codes[i + 2].opcode == OpCodes.Ldc_I4_0 &&
                i + 10 < codes.Count)
            {
                // Пропускаем проверку roomness > 0
                int j = i + 3;
                while (j < codes.Count - 5)
                {
                    // Ищем baseClimate.Temperature += 5f
                    if (codes[j].opcode == OpCodes.Ldloc_S &&
                        codes[j + 1].opcode == OpCodes.Dup &&
                        codes[j + 2].opcode == OpCodes.Ldfld &&
                        (codes[j + 2].operand as FieldInfo)?.Name == "Temperature" &&
                        codes[j + 3].opcode == OpCodes.Ldc_R4 &&
                        (float)codes[j + 3].operand > 0f &&         // вместо  5f, так как кто-то может изменить значение другим патчем
                        codes[j + 4].opcode == OpCodes.Add &&
                        codes[j + 5].opcode == OpCodes.Stfld)
                    {
                        // Вставляем вызов HeaterBonus после baseClimate.Temperature += 5f
                        var newCodes = new List<CodeInstruction>();

                        // Загружаем baseClimate в стек
                        newCodes.Add(new CodeInstruction(OpCodes.Ldloc_S, codes[j].operand));

                        // Дублируем для последующего использования
                        newCodes.Add(new CodeInstruction(OpCodes.Dup));

                        // Загружаем текущее значение Temperature
                        newCodes.Add(new CodeInstruction(OpCodes.Ldfld,
                            typeof(ClimateCondition).GetField("Temperature")));

                        // Вызываем HeaterBonus
                        newCodes.Add(new CodeInstruction(OpCodes.Ldarg_0)); // this
                        newCodes.Add(new CodeInstruction(OpCodes.Ldarg_0)); // this для upPos
                        newCodes.Add(new CodeInstruction(OpCodes.Ldfld,
                            typeof(BlockEntityFarmland).GetField("upPos", BindingFlags.NonPublic | BindingFlags.Instance)));
                        newCodes.Add(new CodeInstruction(OpCodes.Call, heaterBonusMethod));

                        // Складываем результат
                        newCodes.Add(new CodeInstruction(OpCodes.Add));

                        // Сохраняем обратно в Temperature
                        newCodes.Add(new CodeInstruction(OpCodes.Stfld,
                            typeof(ClimateCondition).GetField("Temperature")));

                        // Вставляем новые инструкции
                        codes.InsertRange(j + 6, newCodes);
                        found = true;
                        break;
                    }
                    j++;
                }
                if (found)
                    break;
            }
        }

        if (!found)
        {
            throw new Exception("FarmlandHeaterPatch: Could not find injection point!");
        }

        return codes;
    }




    

    /// <summary>
    /// Считаем бонус от обогревателей в комнате
    /// </summary>
    /// <param name="farmland"></param>
    /// <param name="upPos"></param>
    /// <returns></returns>
    public static float HeaterBonus(BlockEntityFarmland farmland, BlockPos upPos)
    {
        try
        {
            // Получаем blockFarmland через свойство Block
            BlockFarmland blockFarmland = farmland.Block as BlockFarmland;
            if (blockFarmland == null)
                return -1f;

            // 1) Получаем комнату для грядки
            Room roomForPosition = blockFarmland.roomreg?.GetRoomForPosition(upPos);
            int roomness = roomForPosition == null || roomForPosition.SkylightCount <= roomForPosition.NonSkylightCount || roomForPosition.ExitCount != 0 ? 0 : 1;

            if (roomness<=0)
                return -1f;

            // 2) Получаем мод ElectricalProgressive
            var electricalMod = farmland.Api.ModLoader.GetModSystem<ElectricalProgressive>();
            if (electricalMod == null)
                return -1f;


            int roomVolume = CalculateRoomVolume(roomForPosition);
            if (roomVolume <= 0)
                return -1f;

            //var cond= farmland.Api.World.BlockAccessor.GetClimateAt(farmland.Pos, EnumGetClimateMode.ForSuppliedDate_TemperatureRainfallOnly);

            float totalBonus = 0f;

            var heatersInRoom = new List<BEBehaviorEHeater>();

            // 3) Ищем обогреватели в пределах 20 блоков и в той же комнате
            foreach (var part in electricalMod.Parts.Values)
            {
                if (part.Consumer is BEBehaviorEHeater heater)
                {
                    // Проверяем расстояние
                    if (Math.Abs(heater.Pos.X - upPos.X) <= 20 &&
                        Math.Abs(heater.Pos.Y - upPos.Y) <= 20 &&
                        Math.Abs(heater.Pos.Z - upPos.Z) <= 20)
                    {
                        // Проверяем, находится ли обогреватель в той же комнате
                        if (roomForPosition.Contains(heater.Pos))
                        {
                            heatersInRoom.Add(heater);

                            // 5) Рассчитываем бонус от одного обогревателя
                            float heaterBonus = (1.0f*heater.getPowerReceive()/heater.getPowerRequest())*(5.0f * (40.0f / roomVolume));

                            totalBonus += heaterBonus;
                        }
                    }
                }
            }


            // сообщаем обогревателям в комнате их бонус
            foreach (var heater in heatersInRoom)
            {
                heater.GreenhouseBonus = totalBonus;
            }

            return totalBonus;
        }
        catch (Exception ex)
        {
            farmland.Api?.Logger.Error($"Error in HeaterBonus: {ex}");
            return -1f;
        }
    }



    /// <summary>
    /// Считаем объем комнаты по битовой маске PosInRoom
    /// </summary>
    /// <param name="room"></param>
    /// <returns></returns>
    public static int CalculateRoomVolume(Room room)
    {
        if (room?.PosInRoom == null)
            return 0;

        int sizex = room.Location.X2 - room.Location.X1 + 1;
        int sizey = room.Location.Y2 - room.Location.Y1 + 1;
        int sizez = room.Location.Z2 - room.Location.Z1 + 1;
        int volume = 0;

        for (int dx = 0; dx < sizex; dx++)
        {
            for (int dy = 0; dy < sizey; dy++)
            {
                for (int dz = 0; dz < sizez; dz++)
                {
                    int pindex = (dy * sizez + dz) * sizex + dx;
                    int byteIndex = pindex / 8;
                    int bitIndex = pindex % 8;

                    if (byteIndex < room.PosInRoom.Length &&
                        (room.PosInRoom[byteIndex] & (1 << bitIndex)) != 0)
                    {
                        volume++;
                    }
                }
            }
        }

        return volume;
    }
}
