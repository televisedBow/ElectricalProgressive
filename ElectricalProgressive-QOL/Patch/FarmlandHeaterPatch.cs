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
    private static MethodInfo heaterBonusBerryBushMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonusBerryBush");
    // Reflection для доступа к приватным полям
    private static FieldInfo berryBushRoomRegField = typeof(BlockEntityBerryBush).GetField("roomreg", BindingFlags.NonPublic | BindingFlags.Instance);
    private static HarmonyMethod farmlandTranspilerMethod;
    private static HarmonyMethod berryBushTranspilerMethod;
    private static MethodInfo heaterBonusBeehiveMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonusBeehive");
    private static HarmonyMethod beehiveTranspilerMethod;
    private static FieldInfo beehiveRoomRegField = typeof(BlockEntityBeehive).GetField("roomreg", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo heaterBonusFruitTreeMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonusFruitTree");
    private static HarmonyMethod fruitTreeTranspilerMethod;
    private static FieldInfo fruitTreeRoomRegField = typeof(FruitTreeRootBH).GetField("roomreg", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo heaterBonusFruitTreeGrowingMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonusFruitTreeGrowing");
    private static HarmonyMethod fruitTreeGrowingTranspilerMethod;
    private static MethodInfo heaterBonusSaplingMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonusSapling");
    private static HarmonyMethod saplingTranspilerMethod;


    /// <summary>
    /// Метод для регистрации всех патчей
    /// </summary>
    /// <param name="harmony"></param>
    /// <exception cref="Exception"></exception>
    public static void RegisterPatch(Harmony harmony)
    {
        // Патч для грядки
        var farmlandMethod = typeof(BlockEntityFarmland).GetMethod("Update",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (farmlandMethod == null)
        {
            throw new Exception("Could not find BlockEntityFarmland.Update method!");
        }

        farmlandTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerFarmland", BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(farmlandMethod, transpiler: farmlandTranspilerMethod);

        // Патч для куста ягод
        var berryBushMethod = typeof(BlockEntityBerryBush).GetMethod("CheckGrow",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (berryBushMethod == null)
        {
            throw new Exception("Could not find BlockEntityBerryBush.CheckGrow method!");
        }

        berryBushTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerBerryBush", BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(berryBushMethod, transpiler: berryBushTranspilerMethod);

        // Патч для улья
        var beehiveMethod = typeof(BlockEntityBeehive).GetMethod("TestHarvestable",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (beehiveMethod == null)
        {
            throw new Exception("Could not find BlockEntityBeehive.TestHarvestable method!");
        }

        beehiveTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerBeehive", BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(beehiveMethod, transpiler: beehiveTranspilerMethod);

        // Патч для фруктового дерева
        var fruitTreeMethod = typeof(FruitTreeRootBH).GetMethod("getGreenhouseTempBonus",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[0],
            null);

        if (fruitTreeMethod == null)
        {
            throw new Exception("Could not find FruitTreeRootBH.getGreenhouseTempBonus method!");
        }

        fruitTreeTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerFruitTree", BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(fruitTreeMethod, transpiler: fruitTreeTranspilerMethod);



        var fruitTreeGrowingMethod = typeof(FruitTreeGrowingBranchBH).GetMethod("OnTick",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (fruitTreeGrowingMethod == null)
        {
            throw new Exception("Could not find FruitTreeGrowingBranchBH.OnTick method!");
        }

        fruitTreeGrowingTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerFruitTreeGrowing", BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(fruitTreeGrowingMethod, transpiler: fruitTreeGrowingTranspilerMethod);


        // Патч для саженца
        var saplingMethod = typeof(BlockEntitySapling).GetMethod("CheckGrow",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (saplingMethod == null)
        {
            throw new Exception("Could not find BESapling.CheckGrow method!");
        }

        saplingTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerSapling", BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(saplingMethod, transpiler: saplingTranspilerMethod);
    }

    /// <summary>
    /// Метод для отмены всех патчей
    /// </summary>
    /// <param name="harmony"></param>
    public static void UnregisterPatch(Harmony harmony)
    {
        var farmlandMethod = typeof(BlockEntityFarmland).GetMethod("Update",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (farmlandMethod != null)
        {
            harmony.Unpatch(farmlandMethod, farmlandTranspilerMethod.method);
        }

        var berryBushMethod = typeof(BlockEntityBerryBush).GetMethod("CheckGrow",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (berryBushMethod != null)
        {
            harmony.Unpatch(berryBushMethod, berryBushTranspilerMethod.method);
        }

        var beehiveMethod = typeof(BlockEntityBeehive).GetMethod("TestHarvestable",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (beehiveMethod != null)
        {
            harmony.Unpatch(beehiveMethod, beehiveTranspilerMethod.method);
        }

        var fruitTreeMethod = typeof(FruitTreeRootBH).GetMethod("getGreenhouseTempBonus",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[0],
            null);

        if (fruitTreeMethod != null)
        {
            harmony.Unpatch(fruitTreeMethod, fruitTreeTranspilerMethod.method);
        }

        var fruitTreeGrowingMethod = typeof(FruitTreeGrowingBranchBH).GetMethod("OnTick",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (fruitTreeGrowingMethod != null)
        {
            harmony.Unpatch(fruitTreeGrowingMethod, fruitTreeGrowingTranspilerMethod.method);
        }


        var saplingMethod = typeof(BlockEntitySapling).GetMethod("CheckGrow",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new Type[] { typeof(float) },
            null);

        if (saplingMethod != null)
        {
            harmony.Unpatch(saplingMethod, saplingTranspilerMethod.method);
        }
    }

    /// <summary>
    /// Транспайлер для BlockEntityFarmland.Update
    /// </summary>
    static IEnumerable<CodeInstruction> TranspilerFarmland(IEnumerable<CodeInstruction> instructions)
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
                        (float)codes[j + 3].operand > 0f &&
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
            throw new Exception("FarmlandHeaterPatch: Could not find injection point for farmland!");
        }

        return codes;
    }

    /// <summary>
    /// Транспайлер для BlockEntityBerryBush.CheckGrow
    /// </summary>
    static IEnumerable<CodeInstruction> TranspilerBerryBush(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var found = false;

        for (int i = 0; i < codes.Count - 2; i++)
        {
            // Ищем последовательность: roomness > 0 -> temperature += 5
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
                    // Ищем temperature += 5
                    if (codes[j].opcode == OpCodes.Ldloc_S &&
                        codes[j + 1].opcode == OpCodes.Ldc_R4 &&
                        (float)codes[j + 1].operand > 0f &&
                        codes[j + 2].opcode == OpCodes.Add &&
                        codes[j + 3].opcode == OpCodes.Stloc_S)
                    {
                        // Вставляем вызов HeaterBonusBerryBush после temperature += 5
                        var newCodes = new List<CodeInstruction>();

                        // Загружаем temperature в стек
                        newCodes.Add(new CodeInstruction(OpCodes.Ldloc_S, codes[j].operand));

                        // Вызываем HeaterBonusBerryBush
                        newCodes.Add(new CodeInstruction(OpCodes.Ldarg_0)); // this
                        newCodes.Add(new CodeInstruction(OpCodes.Call, heaterBonusBerryBushMethod));

                        // Складываем результат
                        newCodes.Add(new CodeInstruction(OpCodes.Add));

                        // Сохраняем обратно в temperature
                        newCodes.Add(new CodeInstruction(OpCodes.Stloc_S, codes[j + 3].operand));

                        // Вставляем новые инструкции
                        codes.InsertRange(j + 4, newCodes);
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
            throw new Exception("FarmlandHeaterPatch: Could not find injection point for berry bush!");
        }

        return codes;
    }



    static IEnumerable<CodeInstruction> TranspilerBeehive(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var found = false;

        for (int i = 0; i < codes.Count - 2; i++)
        {
            // Ищем последовательность: roomness > 0 -> temp += 5
            if (codes[i].opcode == OpCodes.Ldarg_0 &&
                codes[i + 1].opcode == OpCodes.Ldfld &&
                (codes[i + 1].operand as FieldInfo)?.Name == "roomness" &&
                codes[i + 2].opcode == OpCodes.Ldc_R4 &&
                (float)codes[i + 2].operand == 0f &&
                i + 10 < codes.Count)
            {
                // Пропускаем проверку roomness > 0
                int j = i + 3;
                while (j < codes.Count - 5)
                {
                    // Ищем temp += 5
                    if (codes[j].opcode == OpCodes.Ldloc_1 &&
                        codes[j + 1].opcode == OpCodes.Ldc_R4 &&
                        (float)codes[j + 1].operand > 0f &&
                        codes[j + 2].opcode == OpCodes.Add &&
                        codes[j + 3].opcode == OpCodes.Stloc_1)
                    {
                        // Вставляем вызов HeaterBonusBeehive после temp += 5
                        var newCodes = new List<CodeInstruction>();

                        // Загружаем temp в стек
                        newCodes.Add(new CodeInstruction(OpCodes.Ldloc_1, codes[j].operand));

                        // Вызываем HeaterBonusBeehive
                        newCodes.Add(new CodeInstruction(OpCodes.Ldarg_0)); // this
                        newCodes.Add(new CodeInstruction(OpCodes.Call, heaterBonusBeehiveMethod));

                        // Складываем результат
                        newCodes.Add(new CodeInstruction(OpCodes.Add));

                        // Сохраняем обратно в temp
                        newCodes.Add(new CodeInstruction(OpCodes.Stloc_1, codes[j + 3].operand));

                        // Вставляем новые инструкции
                        codes.InsertRange(j + 4, newCodes);
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
            throw new Exception("FarmlandHeaterPatch: Could not find injection point for beehive!");
        }

        return codes;
    }

    // Добавить новый транспайлер для FruitTreeRootBH с проверкой roomness
    static IEnumerable<CodeInstruction> TranspilerFruitTree(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var found = false;

        for (int i = 0; i < codes.Count - 4; i++)
        {
            // Ищем последовательность: roomness > 0 -> return 5;
            // IL_006d: ldc.i4.0
            // IL_006e: ble.s IL_0076
            // IL_0070: ldc.r4 5
            // IL_0075: ret
            if (codes[i].opcode == OpCodes.Ldc_I4_0 &&
                codes[i + 1].opcode == OpCodes.Ble_S &&
                codes[i + 2].opcode == OpCodes.Ldc_R4 &&
                (float)codes[i + 2].operand > 0f &&
                codes[i + 3].opcode == OpCodes.Ret)
            {
                // Вставляем вызов HeaterBonusFruitTree перед возвратом
                var newCodes = new List<CodeInstruction>();

                // Загружаем текущее значение 5
                newCodes.Add(new CodeInstruction(OpCodes.Ldc_R4, codes[i + 2].operand));

                // Вызываем HeaterBonusFruitTree
                newCodes.Add(new CodeInstruction(OpCodes.Ldarg_0)); // this
                newCodes.Add(new CodeInstruction(OpCodes.Call, heaterBonusFruitTreeMethod));

                // Складываем результат (5 + бонус от обогревателей)
                newCodes.Add(new CodeInstruction(OpCodes.Add));

                // Возвращаем результат
                newCodes.Add(new CodeInstruction(OpCodes.Ret));

                // Заменяем старый return 5 на новую последовательность
                codes.RemoveRange(i + 2, 2); // Удаляем ldc.r4 5 и ret
                codes.InsertRange(i + 2, newCodes);
                found = true;
                break;
            }
        }

        if (!found)
        {
            throw new Exception("FarmlandHeaterPatch: Could not find injection point for fruit tree!");
        }

        return codes;
    }



    // Добавить новый транспайлер для FruitTreeGrowingBranchBH.OnTick
    static IEnumerable<CodeInstruction> TranspilerFruitTreeGrowing(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var found = false;

        for (int i = 0; i < codes.Count - 2; i++)
        {
            // Ищем последовательность: 
            // ldc.r4 12
            // bge.un.s IL_0255
            if (codes[i].opcode == OpCodes.Ldc_R4 &&
                (float)codes[i].operand == 12f &&
                codes[i + 1].opcode == OpCodes.Bge_Un_S)
            {
                // Нашли сравнение температуры с 12
                // Вставляем вызов HeaterBonusFruitTreeGrowing перед сравнением

                // Создаем новые инструкции для вставки перед сравнением
                var newCodes = new List<CodeInstruction>();

                // Вызываем HeaterBonusFruitTreeGrowing (результат будет в стеке)
                newCodes.Add(new CodeInstruction(OpCodes.Ldarg_0)); // this
                newCodes.Add(new CodeInstruction(OpCodes.Call, heaterBonusFruitTreeGrowingMethod));

                // Складываем температуру (уже в стеке) с бонусом
                newCodes.Add(new CodeInstruction(OpCodes.Add));

                // Вставляем новые инструкции перед сравнением
                codes.InsertRange(i, newCodes);
                found = true;
                break;
            }
        }

        if (!found)
        {
            throw new Exception("FarmlandHeaterPatch: Could not find injection point for fruit tree growing!");
        }

        return codes;
    }


    static IEnumerable<CodeInstruction> TranspilerSapling(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var found = false;

        for (int i = 0; i < codes.Count - 1; i++)
        {
            // Ищем последовательность: 
            // ldc.r4 5
            // bge.un.s
            if (codes[i].opcode == OpCodes.Ldc_R4 &&
                (float)codes[i].operand == 5f &&
                codes[i + 1].opcode == OpCodes.Bge_Un_S)
            {
                // Нашли сравнение температуры с 5
                // Вставляем вызов HeaterBonusSapling перед сравнением

                // Создаем новые инструкции для вставки перед сравнением
                var newCodes = new List<CodeInstruction>();

                // Вызываем HeaterBonusSapling (результат будет в стеке)
                newCodes.Add(new CodeInstruction(OpCodes.Ldarg_0)); // this
                newCodes.Add(new CodeInstruction(OpCodes.Call, heaterBonusSaplingMethod));

                // Складываем температуру (уже в стеке) с бонусом
                newCodes.Add(new CodeInstruction(OpCodes.Add));

                // Вставляем новые инструкции перед сравнением
                codes.InsertRange(i, newCodes);
                found = true;
                break;
            }
        }

        if (!found)
        {
            throw new Exception("FarmlandHeaterPatch: Could not find injection point for sapling!");
        }

        return codes;
    }

    // Добавить новый метод для расчета бонуса саженца
    public static float HeaterBonusSapling(BlockEntitySapling sapling)
    {
        try
        {
            // Получаем RoomRegistry из API
            var roomreg = sapling.Api.ModLoader.GetModSystem<RoomRegistry>();
            if (roomreg == null)
                return 0;

            // Получаем комнату для позиции саженца
            Room roomForPosition = roomreg.GetRoomForPosition(sapling.Pos);
            if (roomForPosition == null)
                return 0;

            // Проверяем условия комнаты (как в оригинальном коде)
            if (roomForPosition.SkylightCount <= roomForPosition.NonSkylightCount || roomForPosition.ExitCount != 0)
                return 0;

            // Получаем мод ElectricalProgressive
            var electricalMod = sapling.Api.ModLoader.GetModSystem<ElectricalProgressive>();
            if (electricalMod == null)
                return 0;

            int roomVolume = CalculateRoomVolume(roomForPosition);
            if (roomVolume <= 0)
                return 0;

            float totalBonus = 0f;
            var heatersInRoom = new List<BEBehaviorEHeater>();

            // Ищем обогреватели в пределах 20 блоков и в той же комнате
            foreach (var part in electricalMod.Parts.Values)
            {
                if (part.Consumer is BEBehaviorEHeater heater)
                {
                    // Проверяем расстояние
                    if (Math.Abs(heater.Pos.X - sapling.Pos.X) <= 20 &&
                        Math.Abs(heater.Pos.Y - sapling.Pos.Y) <= 20 &&
                        Math.Abs(heater.Pos.Z - sapling.Pos.Z) <= 20)
                    {
                        // Проверяем, находится ли обогреватель в той же комнате
                        if (roomForPosition.Contains(heater.Pos))
                        {
                            heatersInRoom.Add(heater);

                            // Рассчитываем бонус от одного обогревателя
                            float heaterBonus = (1.0f * heater.getPowerReceive() / heater.getPowerRequest()) * (5.0f * (40.0f / roomVolume));

                            totalBonus += heaterBonus;
                        }
                    }
                }
            }

            // Сообщаем обогревателям в комнате их бонус
            foreach (var heater in heatersInRoom)
            {
                heater.GreenhouseBonus = totalBonus;
            }

            return totalBonus;
        }
        catch (Exception ex)
        {
            sapling.Api?.Logger.Error($"Error in HeaterBonusSapling: {ex}");
            return 0;
        }
    }


    // Добавить новый метод для расчета бонуса роста фруктового дерева
    public static float HeaterBonusFruitTreeGrowing(FruitTreeGrowingBranchBH fruitTreeGrowing)
    {
        try
        {
            // Получаем RoomRegistry из API
            var roomreg = fruitTreeGrowing.Api.ModLoader.GetModSystem<RoomRegistry>();
            if (roomreg == null)
                return 0;

            // Получаем комнату для позиции дерева
            Room roomForPosition = roomreg.GetRoomForPosition(fruitTreeGrowing.Blockentity.Pos);
            if (roomForPosition == null)
                return 0;

            // Проверяем условия комнаты (как в оригинальном коде)
            if (roomForPosition.SkylightCount <= roomForPosition.NonSkylightCount || roomForPosition.ExitCount != 0)
                return 0;

            // Получаем мод ElectricalProgressive
            var electricalMod = fruitTreeGrowing.Api.ModLoader.GetModSystem<ElectricalProgressive>();
            if (electricalMod == null)
                return 0;

            int roomVolume = CalculateRoomVolume(roomForPosition);
            if (roomVolume <= 0)
                return 0;

            float totalBonus = 0f;
            var heatersInRoom = new List<BEBehaviorEHeater>();

            // Ищем обогреватели в пределах 20 блоков и в той же комнате
            foreach (var part in electricalMod.Parts.Values)
            {
                if (part.Consumer is BEBehaviorEHeater heater)
                {
                    // Проверяем расстояние
                    if (Math.Abs(heater.Pos.X - fruitTreeGrowing.Blockentity.Pos.X) <= 20 &&
                        Math.Abs(heater.Pos.Y - fruitTreeGrowing.Blockentity.Pos.Y) <= 20 &&
                        Math.Abs(heater.Pos.Z - fruitTreeGrowing.Blockentity.Pos.Z) <= 20)
                    {
                        // Проверяем, находится ли обогреватель в той же комнате
                        if (roomForPosition.Contains(heater.Pos))
                        {
                            heatersInRoom.Add(heater);

                            // Рассчитываем бонус от одного обогревателя
                            float heaterBonus = (1.0f * heater.getPowerReceive() / heater.getPowerRequest()) * (5.0f * (40.0f / roomVolume));

                            totalBonus += heaterBonus;
                        }
                    }
                }
            }

            // Сообщаем обогревателям в комнате их бонус
            foreach (var heater in heatersInRoom)
            {
                heater.GreenhouseBonus = totalBonus;
            }

            return totalBonus;
        }
        catch (Exception ex)
        {
            fruitTreeGrowing.Api?.Logger.Error($"Error in HeaterBonusFruitTreeGrowing: {ex}");
            return 0;
        }
    }





    /// <summary>
    /// Считаем бонус от обогревателей в комнате для грядки
    /// </summary>
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

            if (roomness <= 0)
                return -1f;

            // 2) Получаем мод ElectricalProgressive
            var electricalMod = farmland.Api.ModLoader.GetModSystem<ElectricalProgressive>();
            if (electricalMod == null)
                return -1f;

            int roomVolume = CalculateRoomVolume(roomForPosition);
            if (roomVolume <= 0)
                return -1f;

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
                            float heaterBonus = (1.0f * heater.getPowerReceive() / heater.getPowerRequest()) * (5.0f * (40.0f / roomVolume));

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
    /// Считаем бонус от обогревателей в комнате для куста ягод
    /// </summary>
    public static float HeaterBonusBerryBush(BlockEntityBerryBush berryBush)
    {
        try
        {

            // Получаем roomreg через reflection
            RoomRegistry roomreg = berryBushRoomRegField?.GetValue(berryBush) as RoomRegistry;
            if (roomreg == null)
            {
                // Альтернативный способ получения RoomRegistry
                roomreg = berryBush.Api.ModLoader.GetModSystem<RoomRegistry>();
                if (roomreg == null)
                    return -1f;
            }

            // 1) Получаем комнату для куста
            Room roomForPosition = roomreg.GetRoomForPosition(berryBush.Pos);
            if (roomForPosition == null)
                return -1f;

            // 2) Получаем мод ElectricalProgressive
            var electricalMod = berryBush.Api.ModLoader.GetModSystem<ElectricalProgressive>();
            if (electricalMod == null)
                return -1f;

            int roomVolume = CalculateRoomVolume(roomForPosition);
            if (roomVolume <= 0)
                return -1f;

            float totalBonus = 0f;
            var heatersInRoom = new List<BEBehaviorEHeater>();

            // 3) Ищем обогреватели в пределах 20 блоков и в той же комнате
            foreach (var part in electricalMod.Parts.Values)
            {
                if (part.Consumer is BEBehaviorEHeater heater)
                {
                    // Проверяем расстояние
                    if (Math.Abs(heater.Pos.X - berryBush.Pos.X) <= 20 &&
                        Math.Abs(heater.Pos.Y - berryBush.Pos.Y) <= 20 &&
                        Math.Abs(heater.Pos.Z - berryBush.Pos.Z) <= 20)
                    {
                        // Проверяем, находится ли обогреватель в той же комнате
                        if (roomForPosition.Contains(heater.Pos))
                        {
                            heatersInRoom.Add(heater);

                            // 4) Рассчитываем бонус от одного обогревателя
                            float heaterBonus = (1.0f * heater.getPowerReceive() / heater.getPowerRequest()) * (5.0f * (40.0f / roomVolume));

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
            berryBush.Api?.Logger.Error($"Error in HeaterBonusBerryBush: {ex}");
            return -1f;
        }
    }



    public static float HeaterBonusBeehive(BlockEntityBeehive beehive)
    {
        try
        {

            // Получаем roomreg через reflection
            RoomRegistry roomreg = beehiveRoomRegField?.GetValue(beehive) as RoomRegistry;
            if (roomreg == null)
            {
                roomreg = beehive.Api.ModLoader.GetModSystem<RoomRegistry>();
                if (roomreg == null)
                    return 0;
            }

            // Получаем комнату для улья
            Room roomForPosition = roomreg.GetRoomForPosition(beehive.Pos);
            if (roomForPosition == null)
                return 0;

            // Получаем мод ElectricalProgressive
            var electricalMod = beehive.Api.ModLoader.GetModSystem<ElectricalProgressive>();
            if (electricalMod == null)
                return 0;

            int roomVolume = CalculateRoomVolume(roomForPosition);
            if (roomVolume <= 0)
                return 0;

            float totalBonus = 0f;
            var heatersInRoom = new List<BEBehaviorEHeater>();

            // Ищем обогреватели в пределах 20 блоков и в той же комнате
            foreach (var part in electricalMod.Parts.Values)
            {
                if (part.Consumer is BEBehaviorEHeater heater)
                {
                    // Проверяем расстояние
                    if (Math.Abs(heater.Pos.X - beehive.Pos.X) <= 20 &&
                        Math.Abs(heater.Pos.Y - beehive.Pos.Y) <= 20 &&
                        Math.Abs(heater.Pos.Z - beehive.Pos.Z) <= 20)
                    {
                        // Проверяем, находится ли обогреватель в той же комнате
                        if (roomForPosition.Contains(heater.Pos))
                        {
                            heatersInRoom.Add(heater);

                            // Рассчитываем бонус от одного обогревателя
                            float heaterBonus = (1.0f * heater.getPowerReceive() / heater.getPowerRequest()) * (5.0f * (40.0f / roomVolume));

                            totalBonus += heaterBonus;
                        }
                    }
                }
            }

            // Сообщаем обогревателям в комнате их бонус
            foreach (var heater in heatersInRoom)
            {
                heater.GreenhouseBonus = totalBonus;
            }

            return totalBonus;
        }
        catch (Exception ex)
        {
            beehive.Api?.Logger.Error($"Error in HeaterBonusBeehive: {ex}");
            return 0;
        }
    }





    // Добавить новый метод для расчета бонуса фруктового дерева
    public static float HeaterBonusFruitTree(FruitTreeRootBH fruitTree)
    {
        try
        {
            // Получаем roomreg через reflection
            RoomRegistry roomreg = fruitTreeRoomRegField?.GetValue(fruitTree) as RoomRegistry;
            if (roomreg == null)
            {
                roomreg = fruitTree.Api.ModLoader.GetModSystem<RoomRegistry>();
                if (roomreg == null)
                    return 0;
            }

            // Получаем комнату для дерева
            Room roomForPosition = roomreg.GetRoomForPosition(fruitTree.Blockentity.Pos);
            if (roomForPosition == null)
                return 0;

            // Получаем мод ElectricalProgressive
            var electricalMod = fruitTree.Api.ModLoader.GetModSystem<ElectricalProgressive>();
            if (electricalMod == null)
                return 0;

            int roomVolume = CalculateRoomVolume(roomForPosition);
            if (roomVolume <= 0)
                return 0;

            float totalBonus = 0f;
            var heatersInRoom = new List<BEBehaviorEHeater>();

            // Ищем обогреватели в пределах 20 блоков и в той же комнате
            foreach (var part in electricalMod.Parts.Values)
            {
                if (part.Consumer is BEBehaviorEHeater heater)
                {
                    // Проверяем расстояние
                    if (Math.Abs(heater.Pos.X - fruitTree.Blockentity.Pos.X) <= 20 &&
                        Math.Abs(heater.Pos.Y - fruitTree.Blockentity.Pos.Y) <= 20 &&
                        Math.Abs(heater.Pos.Z - fruitTree.Blockentity.Pos.Z) <= 20)
                    {
                        // Проверяем, находится ли обогреватель в той же комнате
                        if (roomForPosition.Contains(heater.Pos))
                        {
                            heatersInRoom.Add(heater);

                            // Рассчитываем бонус от одного обогревателя
                            float heaterBonus = (1.0f * heater.getPowerReceive() / heater.getPowerRequest()) * (5.0f * (40.0f / roomVolume));

                            totalBonus += heaterBonus;
                        }
                    }
                }
            }

            // Сообщаем обогревателям в комнате их бонус
            foreach (var heater in heatersInRoom)
            {
                heater.GreenhouseBonus = totalBonus;
            }

            return totalBonus;
        }
        catch (Exception ex)
        {
            fruitTree.Api?.Logger.Error($"Error in HeaterBonusFruitTree: {ex}");
            return -1;
        }
    }





    /// <summary>
    /// Считаем объем комнаты по битовой маске PosInRoom
    /// </summary>
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