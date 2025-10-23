using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ElectricalProgressive.RecipeSystem.Recipe;

public class PressRecipe : IByteSerializable, IRecipeBase<PressRecipe>
{
    public string Code;

    public double EnergyOperation;

    public AssetLocation Name { get; set; }

    public bool Enabled { get; set; } = true;

    IRecipeIngredient[] IRecipeBase<PressRecipe>.Ingredients => Ingredients;

    IRecipeOutput IRecipeBase<PressRecipe>.Output => Output;

    public CraftingRecipeIngredient[] Ingredients;

    public JsonItemStack Output;

    // Новые поля для второго выхода
    public JsonItemStack SecondaryOutput;
    public float SecondaryOutputChance = 0f; // Шанс в диапазоне 0-1 (0-100%)


    public PressRecipe Clone()
    {
        var ingredients = new CraftingRecipeIngredient[Ingredients.Length];
        for (var i = 0; i < Ingredients.Length; i++)
        {
            ingredients[i] = Ingredients[i].Clone();
        }

        return new PressRecipe()
        {
            EnergyOperation = EnergyOperation,
            Output = Output.Clone(),
            SecondaryOutput = SecondaryOutput?.Clone(),
            SecondaryOutputChance = SecondaryOutputChance,
            Code = Code,
            Enabled = Enabled,
            Name = Name,
            Ingredients = ingredients
        };
    }

    public Dictionary<string, string[]> GetNameToCodeMapping(IWorldAccessor world)
    {
        Dictionary<string, string[]> mappings = new();

        if (Ingredients == null || Ingredients.Length == 0)
            return mappings;

        foreach (var ingred in Ingredients)
        {
            if (!ingred.Code.Path.Contains("*"))
                continue;

            var wildcardStartLen = ingred.Code.Path.IndexOf("*");
            var wildcardEndLen = ingred.Code.Path.Length - wildcardStartLen - 1;

            List<string> codes = [];

            if (ingred.Type == EnumItemClass.Block)
            {
                for (var i = 0; i < world.Blocks.Count; i++)
                {
                    if (world.Blocks[i].Code == null || world.Blocks[i].IsMissing)
                        continue;

                    if (WildcardUtil.Match(ingred.Code, world.Blocks[i].Code))
                    {
                        var code = world.Blocks[i].Code.Path.Substring(wildcardStartLen);
                        var codepart = code.Substring(0, code.Length - wildcardEndLen);
                        if (ingred.AllowedVariants != null && !ingred.AllowedVariants.Contains(codepart))
                            continue;

                        codes.Add(codepart);

                    }
                }
            }
            else
            {
                for (var i = 0; i < world.Items.Count; i++)
                {
                    if (world.Items[i].Code == null || world.Items[i].IsMissing)
                        continue;

                    if (WildcardUtil.Match(ingred.Code, world.Items[i].Code))
                    {
                        var code = world.Items[i].Code.Path.Substring(wildcardStartLen);
                        var codepart = code.Substring(0, code.Length - wildcardEndLen);
                        if (ingred.AllowedVariants != null && !ingred.AllowedVariants.Contains(codepart))
                            continue;

                        codes.Add(codepart);
                    }
                }
            }

            mappings[ingred.Name] = codes.ToArray();
        }

        return mappings;
    }

    public bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
    {
        var ok = true;

        for (var i = 0; i < Ingredients.Length; i++)
        {
            ok &= Ingredients[i].Resolve(world, sourceForErrorLogging);
        }

        ok &= Output.Resolve(world, sourceForErrorLogging);

        if (SecondaryOutput != null)
        {
            ok &= SecondaryOutput.Resolve(world, sourceForErrorLogging);
        }

        return ok;
    }

    public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
    {
        Code = reader.ReadString();
        Ingredients = new CraftingRecipeIngredient[reader.ReadInt32()];

        for (var i = 0; i < Ingredients.Length; i++)
        {
            Ingredients[i] = new CraftingRecipeIngredient();
            Ingredients[i].FromBytes(reader, resolver);
            Ingredients[i].Resolve(resolver, "Press Recipe (FromBytes)");
        }

        Output = new JsonItemStack();
        Output.FromBytes(reader, resolver.ClassRegistry);
        Output.Resolve(resolver, "Press Recipe (FromBytes)");

        // Чтение дополнительного выхода
        var hasSecondaryOutput = reader.ReadBoolean();
        if (hasSecondaryOutput)
        {
            SecondaryOutput = new JsonItemStack();
            SecondaryOutput.FromBytes(reader, resolver.ClassRegistry);
            SecondaryOutput.Resolve(resolver, "Press Recipe Secondary Output (FromBytes)");
            SecondaryOutputChance = reader.ReadSingle();
        }

        EnergyOperation = reader.ReadDouble();
    }

    public void ToBytes(BinaryWriter writer)
    {
        writer.Write(Code);
        writer.Write(Ingredients.Length);
        for (var i = 0; i < Ingredients.Length; i++)
        {
            Ingredients[i].ToBytes(writer);
        }

        Output.ToBytes(writer);

        // Запись дополнительного выхода
        writer.Write(SecondaryOutput != null);
        if (SecondaryOutput != null)
        {
            SecondaryOutput.ToBytes(writer);
            writer.Write(SecondaryOutputChance);
        }

        writer.Write(EnergyOperation);
    }

    public bool Matches(ItemSlot[] inputSlots, out int outputStackSize)
    {
        outputStackSize = 0;

        var matched = PairInput(inputSlots);
        if (matched == null)
            return false;

        outputStackSize = Output.StackSize;

        return outputStackSize >= 0;
    }

    List<KeyValuePair<ItemSlot, CraftingRecipeIngredient>> PairInput(ItemSlot[] inputStacks)
    {
        List<CraftingRecipeIngredient> ingredientList = [..Ingredients];

        Queue<ItemSlot> inputSlotsList = new();
        foreach (var val in inputStacks)
        {
            if (!val.Empty)
            {
                inputSlotsList.Enqueue(val);
            }
        }

        if (inputSlotsList.Count != Ingredients.Length)
        {
            return null;
        }

        List<KeyValuePair<ItemSlot, CraftingRecipeIngredient>> matched = [];

        while (inputSlotsList.Count > 0)
        {
            var inputSlot = inputSlotsList.Dequeue();
            var found = false;

            for (var i = 0; i < ingredientList.Count; i++)
            {
                var ingred = ingredientList[i];

                if (ingred.SatisfiesAsIngredient(inputSlot.Itemstack))
                {
                    matched.Add(new KeyValuePair<ItemSlot, CraftingRecipeIngredient>(inputSlot, ingred));
                    found = true;
                    ingredientList.RemoveAt(i);
                    break;
                }
            }

            if (!found) return null;
        }

        // We're missing ingredients
        if (ingredientList.Count > 0)
        {
            return null;
        }

        return matched;
    }
}