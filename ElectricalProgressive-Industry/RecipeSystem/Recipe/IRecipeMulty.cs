using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ElectricalProgressive.RecipeSystem.Recipe
{
    public interface IRecipeMulty<T>
    {
        AssetLocation Name { get; set; }
        bool Enabled { get; set; }
        Dictionary<string, string[]> GetNameToCodeMapping(IWorldAccessor world);
        bool Resolve(IWorldAccessor world, string sourceForErrorLogging);
        T Clone();
        IRecipeIngredient[] Ingredients { get; }
        // Заменяем единственный Output на массив выходов
        IRecipeOutput[] Outputs { get; }
    }
}