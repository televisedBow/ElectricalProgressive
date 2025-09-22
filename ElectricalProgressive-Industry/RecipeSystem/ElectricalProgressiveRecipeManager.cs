using ElectricalProgressive.RecipeSystem.Recipe;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ElectricalProgressive.RecipeSystem;

public class ElectricalProgressiveRecipeManager : ModSystem
{
    public static List<CentrifugeRecipe> CentrifugeRecipes;
    public static List<HammerRecipe> HammerRecipes;
    public static List<PressRecipe> PressRecipes;
    public static List<DrawingRecipe> DrawingRecipes;
    public static Dictionary<string, (string code, IEnumerable<dynamic> recipes)> machines;

    private ICoreServerAPI api;

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        api.Event.SaveGameLoaded += CentrifugeRecipe;
        api.Event.SaveGameLoaded += HammerRecipe;
        api.Event.SaveGameLoaded += PressRecipe;
        api.Event.SaveGameLoaded += DrawingRecipe;

        machines = new Dictionary<string, (string code, IEnumerable<dynamic> recipes)>(3);
    }

    /// <summary>
    /// Загружает рецепты для электрической центрифуги из JSON-файлов.
    /// </summary>
    public void CentrifugeRecipe()
    {
        CentrifugeRecipes = new List<CentrifugeRecipe>();

        LoadRecipes<CentrifugeRecipe>("Centrifuge Recipe", "recipes/electric/centrifugerecipe", (r) => CentrifugeRecipes.Add(r));
        api.World.Logger.StoryEvent(Lang.Get("electricalprogressiveindustry:recipeloading"));

        machines.Add("ecentrifuge-", ("electricalprogressiveindustry:ecentrifuge-north", ElectricalProgressiveRecipeManager.CentrifugeRecipes));
    }

    /// <summary>
    /// Загружает рецепты для электрического молота из JSON-файлов.
    /// </summary>
    public void HammerRecipe()
    {
        HammerRecipes = new List<HammerRecipe>();
        LoadRecipes<HammerRecipe>("Hammer Recipe", "recipes/electric/hammerrecipe", (r) => HammerRecipes.Add(r));
        api.World.Logger.StoryEvent(Lang.Get("electricalprogressiveindustry:recipeloading"));

        machines.Add("ehammer-", ("electricalprogressiveindustry:ehammer-north", ElectricalProgressiveRecipeManager.HammerRecipes));
    }


    /// <summary>
    /// Загружает рецепты для электрического пресса из JSON-файлов.
    /// </summary>
    public void PressRecipe()
    {
        PressRecipes = new List<PressRecipe>();
        LoadRecipes<PressRecipe>("Press Recipe", "recipes/electric/pressrecipe", (r) => PressRecipes.Add(r));
        api.World.Logger.StoryEvent(Lang.Get("electricalprogressiveindustry:recipeloading"));

        machines.Add("epress-", ("electricalprogressiveindustry:epress-north", ElectricalProgressiveRecipeManager.PressRecipes));
    }


    /// <summary>
    /// Загружает рецепты для волочильного станка из JSON-файлов.
    /// </summary>
    public void DrawingRecipe()
    {
        DrawingRecipes = new List<DrawingRecipe>();
        LoadRecipes<DrawingRecipe>("Drawing Recipe", "recipes/electric/drawingrecipe", (r) => DrawingRecipes.Add(r));
        api.World.Logger.StoryEvent(Lang.Get("electricalprogressiveindustry:recipeloading"));

        machines.Add("edrawing-", ("electricalprogressiveindustry:edrawing-north", ElectricalProgressiveRecipeManager.DrawingRecipes));
    }


    /// <summary>
    /// Загружает рецепты из JSON-файлов и регистрирует их с помощью указанного метода.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="name"></param>
    /// <param name="path"></param>
    /// <param name="RegisterMethod"></param>
    public void LoadRecipes<T>(string name, string path, Action<T> RegisterMethod) where T : IRecipeBase<T>
    {
        Dictionary<AssetLocation, JToken> many = this.api.Assets.GetMany<JToken>(this.api.Server.Logger, path);
        int num = 0;
        int quantityRegistered = 0;
        int quantityIgnored = 0;
        foreach (KeyValuePair<AssetLocation, JToken> keyValuePair in many)
        {
            if (keyValuePair.Value is JObject)
            {
                LoadGenericRecipe<T>(name, keyValuePair.Key, keyValuePair.Value.ToObject<T>(keyValuePair.Key.Domain), RegisterMethod, ref quantityRegistered, ref quantityIgnored);
                ++num;
            }
            if (keyValuePair.Value is JArray)
            {
                foreach (JToken token in keyValuePair.Value as JArray)
                {
                    LoadGenericRecipe<T>(name, keyValuePair.Key, token.ToObject<T>(keyValuePair.Key.Domain), RegisterMethod, ref quantityRegistered, ref quantityIgnored);
                    ++num;
                }
            }
        }
        this.api.World.Logger.Event("{0} {1}s loaded{2}", (object)quantityRegistered, (object)name, quantityIgnored > 0 ? (object)string.Format(" ({0} could not be resolved)", (object)quantityIgnored) : (object)"");
    }


    /// <summary>
    /// Загружает и регистрирует рецепты с поддержкой подстановочных знаков.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="className"></param>
    /// <param name="path"></param>
    /// <param name="recipe"></param>
    /// <param name="RegisterMethod"></param>
    /// <param name="quantityRegistered"></param>
    /// <param name="quantityIgnored"></param>
    private void LoadGenericRecipe<T>(
        string className,
        AssetLocation path,
        T recipe,
        Action<T> RegisterMethod,
        ref int quantityRegistered,
        ref int quantityIgnored)
        where T : IRecipeBase<T>
    {
        if (!recipe.Enabled)
            return;
        if (recipe.Name == (AssetLocation)null)
            recipe.Name = path;

        IServerWorldAccessor world1 = this.api.World;
        Dictionary<string, string[]> nameToCodeMapping = recipe.GetNameToCodeMapping((IWorldAccessor)world1);
        if (nameToCodeMapping.Count > 0)
        {
            List<T> objList = new List<T>();
            int num = 0;
            bool flag1 = true;
            foreach (KeyValuePair<string, string[]> keyValuePair in nameToCodeMapping)
            {
                if (flag1)
                    num = keyValuePair.Value.Length;
                else
                    num *= keyValuePair.Value.Length;
                flag1 = false;
            }
            bool flag2 = true;
            foreach (KeyValuePair<string, string[]> keyValuePair in nameToCodeMapping)
            {
                string key = keyValuePair.Key;
                string[] strArray = keyValuePair.Value;
                for (int index = 0; index < num; ++index)
                {
                    T obj2;
                    if (flag2)
                        objList.Add(obj2 = (T)recipe.Clone());
                    else
                        obj2 = objList[index];
                    if (obj2.Ingredients != null)
                    {
                        foreach (IRecipeIngredient ingredient in obj2.Ingredients)
                        {
                            if (ingredient.Name == key)
                                ingredient.Code = ingredient.Code.CopyWithPath(ingredient.Code.Path.Replace("*", strArray[index % strArray.Length]));
                        }
                    }
                    obj2.Output.FillPlaceHolder(keyValuePair.Key, strArray[index % strArray.Length]);

                    // Обработка SecondaryOutput только для двух выходов
                    if (obj2 is HammerRecipe hammerRecipe)
                    {
                        hammerRecipe.SecondaryOutput?.FillPlaceHolder(keyValuePair.Key, strArray[index % strArray.Length]);
                    }

                    if (obj2 is PressRecipe pressRecipe)
                    {
                        pressRecipe.SecondaryOutput?.FillPlaceHolder(keyValuePair.Key, strArray[index % strArray.Length]);
                    }

                }
                flag2 = false;
            }
            if (objList.Count == 0)
                this.api.World.Logger.Warning("{1} file {0} make uses of wildcards, but no blocks or item matching those wildcards were found.", (object)path, (object)className);
            foreach (T obj3 in objList)
            {
                if (!obj3.Resolve((IWorldAccessor)this.api.World, className + " " + (string)path))
                {
                    ++quantityIgnored;
                }
                else
                {
                    RegisterMethod(obj3);
                    ++quantityRegistered;
                }
            }
        }
        else
        {
            if (!recipe.Resolve((IWorldAccessor)this.api.World, className + " " + (string)path))
            {
                ++quantityIgnored;
            }
            else
            {
                RegisterMethod(recipe);
                ++quantityRegistered;
            }
        }
    }

    /// <summary>
    /// Очистка ресурсов при выгрузке мода.
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();


        CentrifugeRecipes?.Clear();
        HammerRecipes?.Clear();
        PressRecipes?.Clear();
        machines?.Clear();
        CentrifugeRecipes = null;
        HammerRecipes = null;
        PressRecipes = null;
        machines = null;

    }
}