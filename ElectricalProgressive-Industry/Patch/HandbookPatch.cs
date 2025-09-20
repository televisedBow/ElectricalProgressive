using Cairo;
using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Patch;

public class HandbookPatch
{
    private static ICoreClientAPI _capi;
    private static readonly ConcurrentDictionary<string, ItemStack> _stackCache = new();

    public static void ApplyPatches(ICoreClientAPI clientApi)
    {
        _capi = clientApi;
        var harmony = new Harmony("electricalprogressive.handbook.patches");
        harmony.Patch(
            typeof(CollectibleBehaviorHandbookTextAndExtraInfo).GetMethod("GetHandbookInfo"),
            postfix: new HarmonyMethod(typeof(HandbookPageComposer).GetMethod("AddRecipeInfoPostfix"))
        );
    }

    public static class HandbookPageComposer
    {
        private const float ItemSize = 40.0f;
        private const float LineSpacing = 14f;
        private const float SmallPadding = 2f;
        private const float RecipeSpacing = 14f;

        public static void AddRecipeInfoPostfix(
            CollectibleBehaviorHandbookTextAndExtraInfo __instance,
            ItemSlot inSlot,
            ICoreClientAPI capi,
            ItemStack[] allStacks,
            ActionConsumable<string> openDetailPageFor,
            ref RichTextComponentBase[] __result)
        {
            try
            {
                var stack = inSlot.Itemstack;
                if (stack == null) return;

                var components = new List<RichTextComponentBase>(__result);
                bool haveText = components.Count > 0;

                CheckAndAddRecipes(components, capi, stack, openDetailPageFor, ref haveText);

                __result = components.ToArray();
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"Handbook error: {ex}");
            }
        }

        private static void CheckAndAddRecipes(
            List<RichTextComponentBase> components,
            ICoreClientAPI capi,
            ItemStack stack,
            ActionConsumable<string> openDetailPageFor,
            ref bool haveText)
        {


            var machine = ElectricalProgressiveRecipeManager.machines.FirstOrDefault(m => stack.Collectible.Code.Path.StartsWith(m.Key));

            if (machine.Value != default)
            {
                if (machine.Value.recipes != null)
                {
                    AddMachineInfo(components, capi, machine.Value.code,
                        GetCachedTranslation("electricalprogressive:produced-in"),
                        openDetailPageFor,
                        ref haveText);
                    AddRecipes(components, capi, machine.Value.recipes, openDetailPageFor);
                }
            }
            else
            {
                foreach (var m in ElectricalProgressiveRecipeManager.machines)
                {
                    if (m.Value.recipes == null) continue;

                    var relevantRecipes = m.Value.recipes
                        .Where(r => IsItemInRecipe(stack, r))
                        .ToList();

                    if (relevantRecipes.Count > 0)
                    {
                        AddMachineInfo(components, capi, m.Value.code,
                            GetCachedTranslation("electricalprogressive:produced-in"),
                            openDetailPageFor,
                            ref haveText);
                        AddRecipes(components, capi, relevantRecipes, openDetailPageFor);
                    }
                }
            }
        }

        private static string GetCachedTranslation(string key)
        {
            return Lang.Get(key);
        }

        private static void AddMachineInfo(
            List<RichTextComponentBase> components,
            ICoreClientAPI capi,
            string machineCode,
            string title,
            ActionConsumable<string> openDetailPageFor,
            ref bool haveText)
        {
            if (haveText)
                components.Add(new ClearFloatTextComponent(capi, LineSpacing));

            haveText = true;

            components.Add(new RichTextComponent(capi, title + " ",
                CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold))
            {
                VerticalAlign = EnumVerticalAlign.Middle,
                Float = EnumFloat.Inline
            });

            var machineStack = GetOrCreateStack(machineCode, 1, capi.World);
            if (machineStack != null)
            {
                var machineIcon = CreateItemStackComponent(capi, machineStack, openDetailPageFor);
                machineIcon.VerticalAlign = EnumVerticalAlign.Middle;
                machineIcon.Float = EnumFloat.Inline;
                components.Add(machineIcon);
            }
        }




        /// <summary>
        /// Добавление рецептов с поддержкой слайд-шоу 
        /// </summary>
        /// <param name="components"></param>
        /// <param name="capi"></param>
        /// <param name="recipes"></param>
        /// <param name="openDetailPageFor"></param>
        private static void AddRecipes(
            List<RichTextComponentBase> components,
            ICoreClientAPI capi,
            IEnumerable<dynamic> recipes,
            ActionConsumable<string> openDetailPageFor)
        {
            components.Add(new ClearFloatTextComponent(capi, SmallPadding));

            // Группируем рецепты по Code
            var groupedRecipes = recipes
                .Select(r => (dynamic)r)
                .GroupBy(r => (string)r.Code)
                .ToList();

            foreach (var group in groupedRecipes)
            {
                var recipeList = group.ToList();
                if (recipeList.Count == 0 || recipeList[0].Code == "default_perish")
                    continue;

                components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));

                // Для групп с одним рецептом отображаем обычным образом
                if (recipeList.Count == 1)
                {
                    AddSingleRecipe(components, capi, recipeList[0], openDetailPageFor);
                    continue;
                }

                string groupKey = group.Key; // уникальный код рецепта

                // Для групп с несколькими рецептами создаем слайд-шоу
                // Собираем все ингредиенты и выходы для слайд-шоу
                var allIngredients = new List<List<ItemStack>>();
                var outputStacks = new List<ItemStack>();
                var secondaryOutputStacks = new List<ItemStack>();


                foreach (var recipe in recipeList)
                {
                    // Обрабатываем ингредиенты
                    int ingIndex = 0;
                    foreach (var ing in recipe.Ingredients)
                    {
                        var resolved = GetOrCreateStack((AssetLocation)ing.Code, (int)ing.Quantity, capi.World);
                        if (resolved != null)
                        {
                            // Добавляем в соответствующий слот
                            if (ingIndex >= allIngredients.Count)
                            {
                                allIngredients.Add(new List<ItemStack>());
                            }
                            allIngredients[ingIndex].Add(resolved);
                            ingIndex++;
                        }
                    }

                    // Обрабатываем выход(ы)
                    var outputStack1 = GetOrCreateStack((AssetLocation)recipe.Output.Code, (int)recipe.Output.Quantity, capi.World);
                    if (outputStack1 != null)
                    {
                        outputStacks.Add(outputStack1);
                    }

                    // только у молота есть второй выход
                    if (recipe is HammerRecipe)
                    {
                        var outputStack2 = GetOrCreateStack((AssetLocation)recipe.SecondaryOutput.Code,
                            (int)recipe.SecondaryOutput.Quantity, capi.World);
                        if (outputStack2 != null)
                        {
                            secondaryOutputStacks.Add(outputStack2);
                        }
                    }
                }

                // Отображаем ингредиенты с слайд-шоу
                bool firstIngredient = true;
                foreach (var ingredientOptions in allIngredients)
                {
                    if (!firstIngredient)
                    {
                        // Плюсик между ингредиентами
                        var plus = new RichTextComponent(capi, "+ ",
                            CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                        {
                            VerticalAlign = EnumVerticalAlign.Middle
                        };
                        components.Add(plus);
                    }

                    var ingredientSlideShow = new SyncedSlideshowItemstackTextComponent(
                        capi,
                        ingredientOptions.ToArray(),
                        40.0,
                        EnumFloat.Inline,
                        (Action<ItemStack>)(cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))),
                        groupKey
                    )
                    {
                        ShowStackSize = true,
                        VerticalAlign = EnumVerticalAlign.Middle
                    };
                    components.Add(ingredientSlideShow);

                    firstIngredient = false;
                }

                // Стрелка
                var arrow = new RichTextComponent(capi, " → ",
                    CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                {
                    VerticalAlign = EnumVerticalAlign.Middle
                };
                components.Add(arrow);

                // Выход с слайд-шоу
                var outputSlideShow = new SyncedSlideshowItemstackTextComponent(
                    capi,
                    outputStacks.ToArray(),
                    40.0,
                    EnumFloat.Inline,
                    (Action<ItemStack>)(cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))),
                    groupKey
                )
                {
                    ShowStackSize = true,
                    VerticalAlign = EnumVerticalAlign.Middle
                };
                
                components.Add(outputSlideShow);

                if (recipeList[0] is HammerRecipe)
                {
                    // Плюсик между ингредиентами
                    var plus = new RichTextComponent(capi, " + ",
                        CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                    {
                        VerticalAlign = EnumVerticalAlign.Middle
                    };
                    components.Add(plus);

                    // Второй выход 
                    var outputSlideShow2 = new SyncedSlideshowItemstackTextComponent(
                        capi,
                        secondaryOutputStacks.ToArray(),
                        40.0,
                        EnumFloat.Inline,
                        (Action<ItemStack>)(cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))),
                        groupKey
                    )
                    {
                        ShowStackSize = true,
                        VerticalAlign = EnumVerticalAlign.Middle
                    };

                    components.Add(outputSlideShow2);

                    // выводим шанс после стака с вторым выходом
                    components.Add(new RichTextComponent(capi,
                        $" ({GetCachedTranslation("electricalprogressive:chance")}: {(int)(recipeList[0].SecondaryOutputChance * 100)} %)",
                        CairoFont.WhiteSmallText()) { VerticalAlign = EnumVerticalAlign.Middle });
                }

                // Добавляем информацию об энергии (берем из первого рецепта в группе)
                components.Add(new RichTextComponent(capi,
                    $"\n{GetCachedTranslation("electricalprogressive:energy-required")}: {recipeList[0].EnergyOperation} {GetCachedTranslation("electricalprogressive:energy-unit")}",
                    CairoFont.WhiteSmallText()));
            }
        }


        /// <summary>
        /// Добавляем рецепт, когда он один
        /// </summary>
        /// <param name="components"></param>
        /// <param name="capi"></param>
        /// <param name="recipe"></param>
        /// <param name="openDetailPageFor"></param>
        private static void AddSingleRecipe(
            List<RichTextComponentBase> components,
            ICoreClientAPI capi,
            dynamic recipe,
            ActionConsumable<string> openDetailPageFor)
        {
            components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));

            bool firstIngredient = true;
            foreach (var ing in recipe.Ingredients)
            {
                if (!firstIngredient)
                {
                    var plus = new RichTextComponent(capi, "+ ",
                        CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                    {
                        VerticalAlign = EnumVerticalAlign.Middle
                    };
                    components.Add(plus);
                }

                var resolved = GetOrCreateStack((AssetLocation)ing.Code, (int)ing.Quantity, capi.World);
                if (resolved != null)
                {
                    components.Add(CreateItemStackComponent(capi, resolved, openDetailPageFor));
                }

                firstIngredient = false;
            }

            var arrow = new RichTextComponent(capi, "→ ",
                CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
            {
                VerticalAlign = EnumVerticalAlign.Middle
            };
            components.Add(arrow);



            // Обрабатываем выход(ы)
            var outputStack1 = GetOrCreateStack((AssetLocation)recipe.Output.Code, (int)recipe.Output.Quantity, capi.World);
            if (outputStack1 != null)
            {
                components.Add(CreateItemStackComponent(capi, outputStack1, openDetailPageFor));
            }

            if (recipe is HammerRecipe)
            {
                // Второй выход +
                var plus = new RichTextComponent(capi, "+ ",
                    CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                {
                    VerticalAlign = EnumVerticalAlign.Middle
                };
                components.Add(plus);

                var outputStack2 = GetOrCreateStack((AssetLocation)recipe.SecondaryOutput.Code,
                    (int)recipe.SecondaryOutput.Quantity, capi.World);
                if (outputStack1 != null)
                {
                    components.Add(CreateItemStackComponent(capi, outputStack2, openDetailPageFor));
                }

                // выводим шанс после стака с вторым выходом
                components.Add(new RichTextComponent(capi,
                    $"({GetCachedTranslation("electricalprogressive:chance")}: {(int)(recipe.SecondaryOutputChance*100)} %)",
                    CairoFont.WhiteSmallText())
                {
                    VerticalAlign = EnumVerticalAlign.Middle
                });
                
            }

            
            components.Add(new RichTextComponent(capi,
                $"\n{GetCachedTranslation("electricalprogressive:energy-required")}: {recipe.EnergyOperation} {GetCachedTranslation("electricalprogressive:energy-unit")}",
                CairoFont.WhiteSmallText()));
        }



        private static ItemstackTextComponent CreateItemStackComponent(
            ICoreClientAPI capi,
            ItemStack stack,
            ActionConsumable<string> openDetailPageFor)
        {
            var component = new ItemstackTextComponent(capi, stack, ItemSize, 10.0, EnumFloat.Inline,
                onStackClicked: (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)))
            {
                ShowStacksize = true,
                VerticalAlign = EnumVerticalAlign.Middle
            };
            return component;
        }

        private static ItemStack GetOrCreateStack(AssetLocation code, int quantity, IWorldAccessor world)
        {
            if (code == null) return null;

            string cacheKey = $"{code}-{quantity}";
            if (_stackCache.TryGetValue(cacheKey, out var cachedStack))
            {
                return cachedStack;
            }

            try
            {
                var item = world.GetItem(code);
                if (item != null)
                {
                    var stack = new ItemStack(item, quantity);
                    _stackCache.TryAdd(cacheKey, stack);
                    return stack;
                }

                var block = world.GetBlock(code);
                if (block != null)
                {
                    var stack = new ItemStack(block, quantity);
                    _stackCache.TryAdd(cacheKey, stack);
                    return stack;
                }

                return null;
            }
            catch (Exception ex)
            {
                _capi?.Logger.Error($"Error resolving item {code}: {ex}");
                return null;
            }
        }

        private static bool IsItemInRecipe(ItemStack stack, dynamic recipe)
        {
            if (stack == null || recipe == null) return false;

            foreach (var ing in recipe.Ingredients)
            {
                var resolved = GetOrCreateStack(ing.Code, (int)ing.Quantity, _capi.World);
                if (resolved != null && resolved.Collectible.Code == stack.Collectible.Code)
                    return true;
            }

            var outputStack = GetOrCreateStack(recipe.Output.Code, (int)recipe.Output.Quantity, _capi.World);
            return outputStack != null && outputStack.Collectible.Code == stack.Collectible.Code;
        }
    }

    public class SyncedSlideshowItemstackTextComponent : SlideshowItemstackTextComponent
    {
        private int CurItemIndex;

        private class GroupState
        {
            public int Index = 0;
            public double LastSwitchTime = 0;
            public int HoverCount = 0; // Количество наведённых элементов в группе
        }

        private static readonly Dictionary<string, GroupState> groups = new();
        private readonly string groupKey;
        private bool isHovered;

        public SyncedSlideshowItemstackTextComponent(
            ICoreClientAPI capi,
            ItemStack[] stacks,
            double size,
            EnumFloat floatType,
            Action<ItemStack> onStackClick,
            string groupKey
        ) : base(capi, stacks, size, floatType, onStackClick)
        {
            this.groupKey = groupKey;
            if (!groups.ContainsKey(groupKey))
            {
                groups[groupKey] = new GroupState();
            }
        }

        public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
        {
            var state = groups[groupKey];
            LineRectangled rect = this.BoundsPerLine[0];

            // Проверяем наведение для текущего элемента
            int x = (int)(this.api.Input.MouseX - renderX + this.renderOffset.X);
            int y = (int)(this.api.Input.MouseY - renderY + this.renderOffset.Y);
            bool nowHovered = rect.PointInside(x, y);

            // Обновляем счётчик наведения
            if (nowHovered && !isHovered)
            {
                state.HoverCount++;
                isHovered = true;
            }
            else if (!nowHovered && isHovered)
            {
                state.HoverCount--;
                isHovered = false;
            }

            // Листаем только если ни один элемент группы не под мышкой
            if (state.HoverCount == 0 && api.World.ElapsedMilliseconds - state.LastSwitchTime > 2000)
            {
                state.Index++;
                state.LastSwitchTime = api.World.ElapsedMilliseconds;
            }

            CurItemIndex = state.Index % this.Itemstacks.Length;

            // Остальной код рендера...
            ItemStack itemStack = this.Itemstacks[CurItemIndex];
            if (this.overrideCurrentItemStack != null)
                itemStack = this.overrideCurrentItemStack();

            this.slot.Itemstack = itemStack;

            ElementBounds bounds = ElementBounds.FixedSize(
                (int)(rect.Width / (double)RuntimeEnv.GUIScale),
                (int)(rect.Height / (double)RuntimeEnv.GUIScale)
            );
            bounds.ParentBounds = this.capi.Gui.WindowBounds;
            bounds.CalcWorldBounds();
            bounds.absFixedX = renderX + rect.X + this.renderOffset.X;
            bounds.absFixedY = renderY + rect.Y + this.renderOffset.Y;
            bounds.absInnerWidth *= this.renderSize / 0.58f;
            bounds.absInnerHeight *= this.renderSize / 0.58f;

            this.api.Render.PushScissor(bounds, true);
            this.api.Render.RenderItemstackToGui(
                this.slot,
                renderX + rect.X + rect.Width * 0.5 + this.renderOffset.X + this.offX,
                renderY + rect.Y + rect.Height * 0.5 + this.renderOffset.Y + this.offY,
                100.0 + this.renderOffset.Z,
                (float)rect.Width * this.renderSize,
                -1,
                showStackSize: this.ShowStackSize
            );
            this.api.Render.PopScissor();

            if (nowHovered && this.ShowTooltip)
            {
                this.RenderItemstackTooltip(this.slot, renderX + x, renderY + y, deltaTime);
            }
        }

        // Добавляем метод для очистки при уничтожении компонента
        public override void Dispose()
        {
            if (isHovered && groups.ContainsKey(groupKey))
            {
                groups[groupKey].HoverCount--;
            }
            base.Dispose();
        }
    }





}