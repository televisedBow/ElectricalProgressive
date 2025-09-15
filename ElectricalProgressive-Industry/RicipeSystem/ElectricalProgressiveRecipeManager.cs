using ElectricalProgressive.RicipeSystem.Recipe;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace ElectricalProgressive.RicipeSystem;


public class ElectricalProgressiveRecipeManager : ModSystem
{
    public static List<CentrifugeRecipe> CentrifugeRecipes;
    public static List<HammerRecipe> HammerRecipes;
    public static List<PressRecipe> PressRecipes;
    public static Dictionary<string, (string code, IEnumerable<dynamic> recipes)> machines;

    private ICoreServerAPI api;

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        api.Event.SaveGameLoaded += CentrifugeRecipe;
        api.Event.SaveGameLoaded += HammerRecipe;
        api.Event.SaveGameLoaded += PressRecipe;

        // инициализация словаря машин с их кодами и рецептами
        machines = new Dictionary<string, (string code, IEnumerable<dynamic> recipes)>(3);
        

    }

    public void CentrifugeRecipe()
    {
        CentrifugeRecipes = new List<CentrifugeRecipe>();
        RecipeLoader recipeLoader = api.ModLoader.GetModSystem<RecipeLoader>();
        recipeLoader.LoadRecipes<CentrifugeRecipe>("Centrifuge Recipe", "recipes/electric/centrifugerecipe", (r) => CentrifugeRecipes.Add(r));
        api.World.Logger.StoryEvent(Lang.Get("electricalprogressiveindustry:recipeloading"));

        // инициализация словаря машин с их кодами и рецептами
        machines.Add("ecentrifuge-", ("electricalprogressiveindustry:ecentrifuge-north", ElectricalProgressiveRecipeManager.CentrifugeRecipes));
    }
    
    public void HammerRecipe()
    {
        HammerRecipes = new List<HammerRecipe>();
        RecipeLoader recipeLoader = api.ModLoader.GetModSystem<RecipeLoader>();
        recipeLoader.LoadRecipes<HammerRecipe>("Hammer Recipe", "recipes/electric/hammerrecipe", (r) => HammerRecipes.Add(r));
        api.World.Logger.StoryEvent(Lang.Get("electricalprogressiveindustry:recipeloading"));

        // инициализация словаря машин с их кодами и рецептами
        machines.Add("ehammer-", ("electricalprogressiveindustry:ehammer-north", ElectricalProgressiveRecipeManager.HammerRecipes));
    }
    
    public void PressRecipe()
    {
        PressRecipes = new List<PressRecipe>();
        RecipeLoader recipeLoader = api.ModLoader.GetModSystem<RecipeLoader>();
        recipeLoader.LoadRecipes<PressRecipe>("Press Recipe", "recipes/electric/pressrecipe", (r) => PressRecipes.Add(r));
        api.World.Logger.StoryEvent(Lang.Get("electricalprogressiveindustry:recipeloading"));

        // инициализация словаря машин с их кодами и рецептами
        machines.Add("epress-", ("electricalprogressiveindustry:epress-north", ElectricalProgressiveRecipeManager.PressRecipes));
    }
}