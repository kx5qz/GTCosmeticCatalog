using BepInEx;
using GorillaNetworking;
using PlayFab.ClientModels;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GTCosmeticCatalog
{
    [BepInPlugin("kx5qz.gtcosmeticcatalog", "GT Cosmetic Catalog", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private bool catalogVisible;
        private Vector2 catalogScrollPosition;
        private List<CosmeticsController.CosmeticItem> catalogItems = new List<CosmeticsController.CosmeticItem>();
        private readonly List<CosmeticsController.CosmeticItem> filteredItems = new List<CosmeticsController.CosmeticItem>();
        private string searchText = string.Empty;
        private string lastSearchText = string.Empty;
        private bool filterNeedsRebuild = true;
        private int currentPage;
        private string cartMessage = string.Empty;
        private CosmeticsController.CosmeticItem selectedItem;
        private bool hasSelectedItem;
        private readonly HashSet<string> availableItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int availableCatalogCount;
        private bool hasLoggedCatalog;

        private const int ItemsPerPage = 40;

        private void Awake()
        {
            Logger.LogInfo("GTCosmeticCatalog loaded!");
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            {
                catalogVisible = !catalogVisible;
            }

            CosmeticsController cosmeticsController = CosmeticsController.instance;
            if (cosmeticsController == null || cosmeticsController.allCosmetics == null)
            {
                return;
            }

            if (!ReferenceEquals(catalogItems, cosmeticsController.allCosmetics))
            {
                catalogItems = cosmeticsController.allCosmetics;
                filteredItems.Clear();
                lastSearchText = string.Empty;
                filterNeedsRebuild = true;
                currentPage = 0;
            }

            UpdateAvailableItems(cosmeticsController);

            if (!hasLoggedCatalog)
            {
                Logger.LogInfo("Loaded " + catalogItems.Count + " cosmetics from the full registry.");
                hasLoggedCatalog = true;
            }
        }

        private void OnGUI()
        {
            if (!catalogVisible)
            {
                return;
            }

            GUI.Window(9182, new Rect(20f, 20f, 820f, 760f), DrawCatalogWindow, "GT Cosmetic Catalog");
        }

        private void DrawCatalogWindow(int windowId)
        {
            CosmeticsController cosmeticsController = CosmeticsController.instance;
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Full registry: " + catalogItems.Count + " items", GUILayout.Width(230f));
            GUILayout.Label("Balance: " + (cosmeticsController == null ? 0 : cosmeticsController.CurrencyBalance) + " shiny rocks", GUILayout.Width(210f));
            GUILayout.Label("Cart: " + GetCartCount() + " items");
            GUILayout.EndHorizontal();
            GUILayout.Label("Registry-only items may be visible but rejected by the game service.");
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(45f));
            searchText = GUILayout.TextField(searchText, GUILayout.Width(420f));
            if (GUILayout.Button("Clear", GUILayout.Width(55f)))
            {
                searchText = string.Empty;
            }

            GUILayout.EndHorizontal();
            RebuildFilteredItemsIfNeeded();

            int pageCount = Math.Max(1, (filteredItems.Count + ItemsPerPage - 1) / ItemsPerPage);
            currentPage = Math.Min(currentPage, pageCount - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(35f)))
            {
                currentPage = Math.Max(0, currentPage - 1);
            }

            GUILayout.Label("Page " + (currentPage + 1) + " / " + pageCount + " (" + filteredItems.Count + " matches)");
            if (GUILayout.Button(">", GUILayout.Width(35f)))
            {
                currentPage = Math.Min(pageCount - 1, currentPage + 1);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            catalogScrollPosition = GUILayout.BeginScrollView(catalogScrollPosition, GUILayout.Width(585f), GUILayout.Height(570f));
            int firstItem = currentPage * ItemsPerPage;
            int lastItem = Math.Min(firstItem + ItemsPerPage, filteredItems.Count);
            for (int index = firstItem; index < lastItem; index++)
            {
                CosmeticsController.CosmeticItem item = filteredItems[index];
                GUILayout.BeginHorizontal("box");
                GUILayout.BeginVertical();
                GUILayout.Label(GetDisplayName(item), GUILayout.Width(355f));
                GUILayout.Label(item.itemCategory + " | " + item.cost + " shiny rocks | " + (item.IsCollectable ? "collectable" : "registry-only"), GUILayout.Width(355f));
                GUILayout.EndVertical();
                if (GUILayout.Button("Select", GUILayout.Width(70f), GUILayout.Height(42f)))
                {
                    selectedItem = item;
                    hasSelectedItem = true;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            DrawSelectedItemPanel(cosmeticsController);
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(cartMessage))
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label(cartMessage);
                GUILayout.EndVertical();
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void RebuildFilteredItemsIfNeeded()
        {
            if (!filterNeedsRebuild && searchText == lastSearchText)
            {
                return;
            }

            filteredItems.Clear();
            for (int index = 0; index < catalogItems.Count; index++)
            {
                CosmeticsController.CosmeticItem item = catalogItems[index];
                if (!IsAvailable(item))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(searchText) || (GetDisplayName(item) + " " + FormatItem(item)).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredItems.Add(item);
                }
            }

            lastSearchText = searchText;
            filterNeedsRebuild = false;
            currentPage = 0;
            catalogScrollPosition = Vector2.zero;
        }

        private void AddItemToCart(CosmeticsController.CosmeticItem item)
        {
            CosmeticsController cosmeticsController = CosmeticsController.instance;
            if (cosmeticsController == null)
            {
                cartMessage = "Cosmetics controller is not ready.";
                return;
            }

            cosmeticsController.PressWardrobeItemButton(item, false, false);
            cosmeticsController.UpdateShoppingCart();
            cartMessage = item.itemName + " sent to the game's cart flow.";
        }

        private void DrawSelectedItemPanel(CosmeticsController? cosmeticsController)
        {
            GUILayout.BeginVertical("box", GUILayout.Width(210f), GUILayout.Height(570f));
            GUILayout.Label("Selected item");
            if (!hasSelectedItem)
            {
                GUILayout.Label("Select an item to see its details.");
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label(GetDisplayName(selectedItem));
            GUILayout.Label("ID: " + selectedItem.itemName);
            GUILayout.Label("Category: " + selectedItem.itemCategory);
            GUILayout.Label("Cost: " + selectedItem.cost + " shiny rocks");

            bool inCart = cosmeticsController != null && cosmeticsController.currentCart != null && cosmeticsController.currentCart.Contains(selectedItem);
            if (GUILayout.Button(inCart ? "Remove from cart" : "Add to cart"))
            {
                if (cosmeticsController == null)
                {
                    cartMessage = "Cosmetics controller is not ready.";
                }
                else if (inCart)
                {
                    cosmeticsController.RemoveItemFromCart(selectedItem);
                    cosmeticsController.UpdateShoppingCart();
                    cartMessage = GetDisplayName(selectedItem) + " removed from cart.";
                }
                else
                {
                    AddItemToCart(selectedItem);
                }
            }

            bool canAfford = cosmeticsController != null && selectedItem.cost <= cosmeticsController.CurrencyBalance;
            GUI.enabled = canAfford;
            if (GUILayout.Button("Buy now"))
            {
                BuyItem(selectedItem, cosmeticsController);
            }

            GUI.enabled = true;
            if (!canAfford)
            {
                GUILayout.Label("Not enough shiny rocks.");
            }

            GUILayout.EndVertical();
        }

        private void BuyItem(CosmeticsController.CosmeticItem item, CosmeticsController? cosmeticsController)
        {
            if (cosmeticsController == null)
            {
                cartMessage = "Cosmetics controller is not ready.";
                return;
            }

            cosmeticsController.itemToBuy = item;
            cosmeticsController.PurchaseItem();
            cartMessage = "Purchase started for " + GetDisplayName(item) + ".";
        }

        private int GetCartCount()
        {
            CosmeticsController cosmeticsController = CosmeticsController.instance;
            return cosmeticsController == null || cosmeticsController.currentCart == null ? 0 : cosmeticsController.currentCart.Count;
        }

        private string GetDisplayName(CosmeticsController.CosmeticItem item)
        {
            CosmeticsController cosmeticsController = CosmeticsController.instance;
            if (cosmeticsController != null)
            {
                string controllerName = cosmeticsController.GetItemDisplayName(item);
                if (!string.IsNullOrEmpty(controllerName) && controllerName != item.itemName)
                {
                    return controllerName;
                }
            }

            if (!string.IsNullOrEmpty(item.overrideDisplayName) && item.overrideDisplayName != item.itemName)
            {
                return item.overrideDisplayName;
            }

            return string.IsNullOrEmpty(item.displayName) ? item.itemName : item.displayName;
        }

        private void UpdateAvailableItems(CosmeticsController cosmeticsController)
        {
            if (cosmeticsController.catalogItems == null || cosmeticsController.catalogItems.Count == 0)
            {
                return;
            }

            if (availableCatalogCount == cosmeticsController.catalogItems.Count)
            {
                return;
            }

            availableItemIds.Clear();
            foreach (CatalogItem catalogItem in cosmeticsController.catalogItems)
            {
                if (!string.IsNullOrEmpty(catalogItem.ItemId))
                {
                    availableItemIds.Add(catalogItem.ItemId);
                }
            }

            availableCatalogCount = cosmeticsController.catalogItems.Count;
            filterNeedsRebuild = true;
        }

        private bool IsAvailable(CosmeticsController.CosmeticItem item)
        {
            return availableItemIds.Count == 0 || availableItemIds.Contains(item.itemName);
        }

        private static string FormatItem(CosmeticsController.CosmeticItem item)
        {
            string displayName = string.IsNullOrEmpty(item.displayName) ? item.itemName : item.displayName;
            string category = item.itemCategory.ToString();
            string collectable = item.IsCollectable ? "collectable" : "registry-only";
            return displayName + " | " + item.itemName + " | " + category + " | " + item.cost + " shiny rocks | " + collectable;
        }
    }
}