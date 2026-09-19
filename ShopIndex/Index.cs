//     _____ _____ ____  _____    _____ __ __    _____ __ __ ___ _____ _____ 
//    |     |  _  |    \|   __|  | __  |  |  |  |  |  |  |  |  _|     |__   |
//    | | | |     |  |  |   __|  | __ -|_   _|  |    -|-   -|_  |  |  |   __|
//    |_|_|_|__|__|____/|_____|  |_____| |_|    |__|__|__|__|___|__  _|_____|
//                                                                 |__|      
using BepInEx;
using GorillaNetworking;
using PlayFab.ClientModels;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShopIndex
{
    [BepInPlugin("kx5qz.shopindex", "ShopIndex", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private bool catalogVisible;
        private bool cartTabVisible;
        private Vector2 catalogScrollPosition;
        private Vector2 cartScrollPosition;
        private List<CosmeticsController.CosmeticItem> catalogItems = new List<CosmeticsController.CosmeticItem>();
        private readonly List<CosmeticsController.CosmeticItem> filteredItems = new List<CosmeticsController.CosmeticItem>();
        private string searchText = string.Empty;
        private string lastSearchText = string.Empty;
        private string minPriceText = string.Empty;
        private string maxPriceText = string.Empty;
        private string lastMinPriceText = string.Empty;
        private string lastMaxPriceText = string.Empty;
        private bool filterNeedsRebuild = true;
        private int currentPage;
        private string cartMessage = string.Empty;
        private CosmeticsController.CosmeticItem selectedItem;
        private bool hasSelectedItem;
        private bool hideOffSaleItems;
        private readonly HashSet<string> availableItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int availableCatalogCount;
        private bool hasLoggedCatalog;
        private float contentPanelHeight = 570f;
        private readonly Queue<CosmeticsController.CosmeticItem> cartPurchaseQueue = new Queue<CosmeticsController.CosmeticItem>();
        private bool cartPurchaseInProgress;
        private bool cartPurchaseWaitingForResult;
        private bool cartPurchaseStarted;
        private string cartPurchaseInitialStage = string.Empty;

        private const int ItemsPerPage = 40;
        private bool stylesInitialized;
        private GUIStyle windowStyle = null!;
        private GUIStyle panelStyle = null!;
        private GUIStyle toolbarStyle = null!;
        private GUIStyle headingStyle = null!;
        private GUIStyle mutedLabelStyle = null!;
        private GUIStyle itemTitleStyle = null!;
        private GUIStyle itemMetaStyle = null!;
        private GUIStyle itemSelectedStyle = null!;
        private GUIStyle inspectorValueStyle = null!;
        private GUIStyle statusStyle = null!;
        private GUIStyle topBarMetricStyle = null!;
        private GUIStyle textFieldStyle = null!;
        private GUIStyle buttonStyle = null!;
        private GUIStyle toggleStyle = null!;
        private GUIStyle selectedButtonStyle = null!;

        private void Awake()
        {
            Logger.LogInfo("ShopIndex loaded!");
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
            ProcessCartPurchaseQueue(cosmeticsController);

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

            InitializeStyles();
            float uiScale = Mathf.Clamp(Screen.height / 1080f, 0.85f, 1.35f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * uiScale);
            float availableWidth = Screen.width / uiScale;
            float availableHeight = Screen.height / uiScale;
            float windowWidth = Mathf.Min(840f, availableWidth - 40f);
            float windowHeight = Mathf.Min(720f, availableHeight - 40f);
            float statusBarHeight = string.IsNullOrEmpty(cartMessage) ? 0f : 36f;
            // Keep the status strip inside a bezel that matches the rest of the window.
            contentPanelHeight = Mathf.Max(220f, windowHeight - 206f - statusBarHeight);
            GUI.Window(9182, new Rect(20f, 20f, windowWidth, windowHeight), DrawCatalogWindow, string.Empty, windowStyle);
            GUI.matrix = previousMatrix;
        }

        private void DrawCatalogWindow(int windowId)
        {
            CosmeticsController cosmeticsController = CosmeticsController.instance;
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(34f));
            if (GUILayout.Button("SHOPINDEX", cartTabVisible ? buttonStyle : selectedButtonStyle, GUILayout.Width(92f), GUILayout.Height(22f)))
            {
                cartTabVisible = false;
            }

            GUILayout.Space(8f);
            GUILayout.Label("BALANCE: " + (cosmeticsController == null ? 0 : cosmeticsController.CurrencyBalance), topBarMetricStyle, GUILayout.Width(112f), GUILayout.Height(22f));
            GUILayout.Space(8f);
            GUILayout.Label("ITEMS: " + catalogItems.Count, topBarMetricStyle, GUILayout.Width(96f), GUILayout.Height(22f));
            GUILayout.Space(8f);
            if (GUILayout.Button("CART: " + GetCartCount(), cartTabVisible ? selectedButtonStyle : buttonStyle, GUILayout.Width(70f), GUILayout.Height(22f)))
            {
                cartTabVisible = true;
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("F8  CLOSE", mutedLabelStyle);
            GUILayout.EndHorizontal();

            if (cartTabVisible)
            {
                DrawCartTab(cosmeticsController);
                GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
                return;
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(38f));
            GUILayout.Label("FILTERS", headingStyle, GUILayout.Width(58f));
            GUILayout.Label("MIN", mutedLabelStyle, GUILayout.Width(26f));
            minPriceText = GUILayout.TextField(minPriceText, textFieldStyle, GUILayout.Width(54f), GUILayout.Height(24f));
            GUILayout.Label("MAX", mutedLabelStyle, GUILayout.Width(28f));
            maxPriceText = GUILayout.TextField(maxPriceText, textFieldStyle, GUILayout.Width(54f), GUILayout.Height(24f));

            bool nextHideOffSaleItems = GUILayout.Toggle(hideOffSaleItems, "HIDE OFF-SALE", toggleStyle, GUILayout.Width(120f));
            if (nextHideOffSaleItems != hideOffSaleItems)
            {
                hideOffSaleItems = nextHideOffSaleItems;
                filterNeedsRebuild = true;
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(38f));
            GUILayout.Label("SEARCH", headingStyle, GUILayout.Width(58f));
            searchText = GUILayout.TextField(searchText, textFieldStyle, GUILayout.Width(410f), GUILayout.Height(24f));
            if (GUILayout.Button("CLEAR", buttonStyle, GUILayout.Width(65f)))
            {
                searchText = string.Empty;
                minPriceText = string.Empty;
                maxPriceText = string.Empty;
            }

            GUILayout.EndHorizontal();
            RebuildFilteredItemsIfNeeded();

            int pageCount = Math.Max(1, (filteredItems.Count + ItemsPerPage - 1) / ItemsPerPage);
            currentPage = Math.Min(currentPage, pageCount - 1);
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(32f));
            if (GUILayout.Button("<", buttonStyle, GUILayout.Width(34f)))
            {
                currentPage = Math.Max(0, currentPage - 1);
            }

            GUILayout.Label("PAGE " + (currentPage + 1) + " / " + pageCount + "    |    " + filteredItems.Count + " MATCHES", mutedLabelStyle);
            if (GUILayout.Button(">", buttonStyle, GUILayout.Width(34f)))
            {
                currentPage = Math.Min(pageCount - 1, currentPage + 1);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            catalogScrollPosition = GUILayout.BeginScrollView(catalogScrollPosition, GUILayout.Width(590f), GUILayout.Height(contentPanelHeight));
            int firstItem = currentPage * ItemsPerPage;
            int lastItem = Math.Min(firstItem + ItemsPerPage, filteredItems.Count);
            for (int index = firstItem; index < lastItem; index++)
            {
                CosmeticsController.CosmeticItem item = filteredItems[index];
                GUILayout.BeginHorizontal(hasSelectedItem && selectedItem.itemName == item.itemName ? itemSelectedStyle : panelStyle, GUILayout.MinHeight(54f));
                GUILayout.BeginVertical();
                GUILayout.Label(GetDisplayName(item), itemTitleStyle, GUILayout.Width(355f));
                GUILayout.Label(item.itemCategory + "  |  " + item.cost + " shiny rocks  |  " + (item.IsCollectable ? "COLLECTABLE" : "REGISTRY-ONLY"), itemMetaStyle, GUILayout.Width(355f));
                GUILayout.EndVertical();
                if (GUILayout.Button("SELECT", buttonStyle, GUILayout.Width(75f), GUILayout.Height(42f)))
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
                GUILayout.Space(6f);
                GUILayout.BeginVertical(toolbarStyle, GUILayout.Height(30f));
                GUILayout.Label("STATUS  " + cartMessage, statusStyle);
                GUILayout.EndVertical();
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawCartTab(CosmeticsController? cosmeticsController)
        {
            // The Index tab has two filter rows and paging; let the Cart use that saved space for its list.
            float cartPanelHeight = contentPanelHeight + 76f;
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(32f));
            GUILayout.Label("YOUR CART", headingStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(GetCartCount() + " " + (GetCartCount() == 1 ? "ITEM" : "ITEMS"), mutedLabelStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            cartScrollPosition = GUILayout.BeginScrollView(cartScrollPosition, GUILayout.Width(590f), GUILayout.Height(cartPanelHeight));
            if (cosmeticsController == null || cosmeticsController.currentCart == null || cosmeticsController.currentCart.Count == 0)
            {
                GUILayout.BeginVertical(panelStyle, GUILayout.Height(74f));
                GUILayout.Label("YOUR CART IS EMPTY", headingStyle);
                GUILayout.Label("Add cosmetics from the Index tab to see them here.", mutedLabelStyle);
                GUILayout.EndVertical();
            }
            else
            {
                foreach (CosmeticsController.CosmeticItem item in cosmeticsController.currentCart)
                {
                    GUILayout.BeginHorizontal(panelStyle, GUILayout.MinHeight(54f));
                    GUILayout.BeginVertical();
                    GUILayout.Label(GetDisplayName(item), itemTitleStyle, GUILayout.Width(355f));
                    GUILayout.Label(item.itemCategory + "  |  " + item.cost + " shiny rocks", itemMetaStyle, GUILayout.Width(355f));
                    GUILayout.EndVertical();
                    if (GUILayout.Button("REMOVE", buttonStyle, GUILayout.Width(62f), GUILayout.Height(36f)))
                    {
                        cosmeticsController.RemoveItemFromCart(item);
                        cosmeticsController.UpdateShoppingCart();
                        cartMessage = GetDisplayName(item) + " removed from cart.";
                        break;
                    }

                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            DrawCartSummaryPanel(cosmeticsController, cartPanelHeight);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(cartMessage))
            {
                GUILayout.Space(6f);
                GUILayout.BeginVertical(toolbarStyle, GUILayout.Height(30f));
                GUILayout.Label("STATUS  " + cartMessage, statusStyle);
                GUILayout.EndVertical();
            }
        }

        private void DrawCartSummaryPanel(CosmeticsController? cosmeticsController, float panelHeight)
        {
            GUILayout.BeginVertical(panelStyle, GUILayout.Width(220f), GUILayout.Height(panelHeight));
            GUILayout.Label("CART SUMMARY", headingStyle);
            int itemCount = GetCartCount();
            int totalCost = GetCartTotal();
            GUILayout.Space(10f);
            GUILayout.Label("ITEMS", mutedLabelStyle);
            GUILayout.Label(itemCount + " " + (itemCount == 1 ? "ITEM" : "ITEMS"), inspectorValueStyle);
            GUILayout.Space(5f);
            GUILayout.Label("TOTAL", mutedLabelStyle);
            GUILayout.Label(totalCost + " SHINY ROCKS", inspectorValueStyle);
            GUILayout.FlexibleSpace();

            bool canPurchase = cosmeticsController != null && itemCount > 0 && totalCost <= cosmeticsController.CurrencyBalance && !cartPurchaseInProgress;
            GUI.enabled = canPurchase;
            if (GUILayout.Button(cartPurchaseInProgress ? "PURCHASING..." : "PURCHASE ALL", selectedButtonStyle, GUILayout.Height(36f)))
            {
                StartCartPurchase(cosmeticsController!);
            }

            GUI.enabled = true;
            if (itemCount > 0 && cosmeticsController != null && totalCost > cosmeticsController.CurrencyBalance)
            {
                GUILayout.Label("Insufficient balance.", mutedLabelStyle);
            }
            else
            {
                GUILayout.Label("Items are purchased one at a time.", mutedLabelStyle);
            }

            GUILayout.EndVertical();
        }

        private void RebuildFilteredItemsIfNeeded()
        {
            if (!filterNeedsRebuild && searchText == lastSearchText && minPriceText == lastMinPriceText && maxPriceText == lastMaxPriceText)
            {
                return;
            }

            int minimumPrice = ParsePrice(minPriceText);
            int maximumPrice = ParsePrice(maxPriceText);
            filteredItems.Clear();
            for (int index = 0; index < catalogItems.Count; index++)
            {
                CosmeticsController.CosmeticItem item = catalogItems[index];
                if (!IsAvailable(item))
                {
                    continue;
                }

                if (hideOffSaleItems && IsOffSale(item))
                {
                    continue;
                }

                if (minimumPrice >= 0 && item.cost < minimumPrice)
                {
                    continue;
                }

                if (maximumPrice >= 0 && item.cost > maximumPrice)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(searchText) || (GetDisplayName(item) + " " + FormatItem(item)).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredItems.Add(item);
                }
            }

            lastSearchText = searchText;
            lastMinPriceText = minPriceText;
            lastMaxPriceText = maxPriceText;
            filterNeedsRebuild = false;
            currentPage = 0;
            catalogScrollPosition = Vector2.zero;
        }

        private void AddItemToCart(CosmeticsController.CosmeticItem item)
        {
            CosmeticsController cosmeticsController = CosmeticsController.instance;
            if (cosmeticsController == null || cosmeticsController.currentCart == null)
            {
                cartMessage = "Cosmetics controller is not ready.";
                return;
            }

            if (cosmeticsController.currentCart.Contains(item))
            {
                cartMessage = GetDisplayName(item) + " is already in the cart.";
                return;
            }

            cosmeticsController.currentCart.Add(item);
            cosmeticsController.UpdateShoppingCart();
            cartMessage = GetDisplayName(item) + " added to the cart.";
        }

        private void DrawSelectedItemPanel(CosmeticsController? cosmeticsController)
        {
            GUILayout.BeginVertical(panelStyle, GUILayout.Width(220f), GUILayout.Height(contentPanelHeight));
            GUILayout.Label("INSPECTOR", headingStyle);
            if (!hasSelectedItem)
            {
                GUILayout.Space(8f);
                GUILayout.Label("Choose an item from the registry to see its details and purchase options.", mutedLabelStyle);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label(GetDisplayName(selectedItem), itemTitleStyle);
            GUILayout.Space(10f);
            GUILayout.Label("ID", mutedLabelStyle);
            GUILayout.Label(selectedItem.itemName, inspectorValueStyle);
            GUILayout.Space(5f);
            GUILayout.Label("CATEGORY", mutedLabelStyle);
            GUILayout.Label(selectedItem.itemCategory.ToString(), inspectorValueStyle);
            GUILayout.Space(5f);
            GUILayout.Label("COST", mutedLabelStyle);
            GUILayout.Label(selectedItem.cost + " SHINY ROCKS", inspectorValueStyle);
            GUILayout.FlexibleSpace();

            bool inCart = cosmeticsController != null && cosmeticsController.currentCart != null && cosmeticsController.currentCart.Contains(selectedItem);
            if (GUILayout.Button(inCart ? "REMOVE FROM CART" : "ADD TO CART", buttonStyle, GUILayout.Height(32f)))
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
            if (GUILayout.Button("BUY NOW", selectedButtonStyle, GUILayout.Height(36f)))
            {
                BuyItem(selectedItem, cosmeticsController);
            }

            GUI.enabled = true;
            if (!canAfford)
            {
                GUILayout.Label("Insufficient balance.", mutedLabelStyle);
            }

            GUILayout.EndVertical();
        }

        private void InitializeStyles()
        {
            if (stylesInitialized)
            {
                return;
            }
// god help me this is so much code to write just to make a window look nice
            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.padding = new RectOffset(10, 10, 10, 10);
            Texture2D windowBackground = MakeTexture(new Color(0.075f, 0.085f, 0.10f, 1f));
            windowStyle.normal.background = windowBackground;
            windowStyle.hover.background = windowBackground;
            windowStyle.focused.background = windowBackground;
            windowStyle.active.background = windowBackground;
            windowStyle.onNormal.background = windowBackground;
            windowStyle.onHover.background = windowBackground;
            windowStyle.onFocused.background = windowBackground;
            windowStyle.onActive.background = windowBackground;
            windowStyle.normal.textColor = new Color(0.82f, 0.86f, 0.90f);
            windowStyle.hover.textColor = windowStyle.normal.textColor;
            windowStyle.focused.textColor = windowStyle.normal.textColor;
            windowStyle.active.textColor = windowStyle.normal.textColor;
            windowStyle.onNormal.textColor = windowStyle.normal.textColor;
            windowStyle.onHover.textColor = windowStyle.normal.textColor;
            windowStyle.onFocused.textColor = windowStyle.normal.textColor;
            windowStyle.onActive.textColor = windowStyle.normal.textColor;

            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.padding = new RectOffset(8, 8, 8, 8);
            panelStyle.normal.background = MakeTexture(new Color(0.11f, 0.12f, 0.14f, 1f));

            toolbarStyle = new GUIStyle(panelStyle);
            toolbarStyle.normal.background = MakeTexture(new Color(0.13f, 0.14f, 0.17f, 1f));

            headingStyle = new GUIStyle(GUI.skin.label);
            headingStyle.fontStyle = FontStyle.Bold;
            headingStyle.fontSize = 11;
            headingStyle.normal.textColor = new Color(0.42f, 0.70f, 0.92f);

            mutedLabelStyle = new GUIStyle(GUI.skin.label);
            mutedLabelStyle.fontSize = 11;
            mutedLabelStyle.normal.textColor = new Color(0.56f, 0.60f, 0.66f);

            itemTitleStyle = new GUIStyle(GUI.skin.label);
            itemTitleStyle.fontStyle = FontStyle.Bold;
            itemTitleStyle.normal.textColor = new Color(0.88f, 0.90f, 0.94f);

            itemMetaStyle = new GUIStyle(mutedLabelStyle);

            itemSelectedStyle = new GUIStyle(panelStyle);
            itemSelectedStyle.normal.background = MakeTexture(new Color(0.14f, 0.24f, 0.32f, 1f));

            inspectorValueStyle = new GUIStyle(GUI.skin.label);
            inspectorValueStyle.fontSize = 12;
            inspectorValueStyle.wordWrap = true;
            inspectorValueStyle.normal.textColor = new Color(0.84f, 0.88f, 0.93f);

            statusStyle = new GUIStyle(mutedLabelStyle);
            statusStyle.normal.textColor = new Color(0.58f, 0.76f, 0.92f);

            textFieldStyle = new GUIStyle(GUI.skin.label);
            textFieldStyle.alignment = TextAnchor.MiddleLeft;
            textFieldStyle.padding = new RectOffset(7, 7, 4, 4);
            textFieldStyle.border = new RectOffset(4, 4, 4, 4);
            Texture2D textFieldBackground = MakeTexture(new Color(0.07f, 0.08f, 0.10f, 1f));
            textFieldStyle.normal.background = textFieldBackground;
            textFieldStyle.hover.background = textFieldBackground;
            textFieldStyle.focused.background = textFieldBackground;
            textFieldStyle.active.background = textFieldBackground;
            textFieldStyle.onNormal.background = textFieldBackground;
            textFieldStyle.onHover.background = textFieldBackground;
            textFieldStyle.onFocused.background = textFieldBackground;
            textFieldStyle.onActive.background = textFieldBackground;
            textFieldStyle.normal.textColor = new Color(0.88f, 0.90f, 0.94f);
            textFieldStyle.hover.textColor = textFieldStyle.normal.textColor;
            textFieldStyle.focused.textColor = Color.white;
            textFieldStyle.active.textColor = Color.white;
            textFieldStyle.onNormal.textColor = textFieldStyle.normal.textColor;
            textFieldStyle.onHover.textColor = textFieldStyle.hover.textColor;
            textFieldStyle.onFocused.textColor = textFieldStyle.focused.textColor;
            textFieldStyle.onActive.textColor = textFieldStyle.active.textColor;

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 10;
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.normal.background = MakeTexture(new Color(0.18f, 0.20f, 0.24f, 1f));
            buttonStyle.normal.textColor = new Color(0.78f, 0.82f, 0.88f);
            buttonStyle.hover.background = MakeTexture(new Color(0.23f, 0.27f, 0.32f, 1f));
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.background = MakeTexture(new Color(0.14f, 0.16f, 0.20f, 1f));

            toggleStyle = new GUIStyle(buttonStyle);
            toggleStyle.onNormal.background = MakeTexture(new Color(0.20f, 0.42f, 0.58f, 1f));
            toggleStyle.onHover.background = toggleStyle.onNormal.background;
            toggleStyle.onFocused.background = toggleStyle.onNormal.background;
            toggleStyle.onActive.background = toggleStyle.onNormal.background;
            toggleStyle.onNormal.textColor = Color.white;
            toggleStyle.onHover.textColor = Color.white;
            toggleStyle.onFocused.textColor = Color.white;
            toggleStyle.onActive.textColor = Color.white;

            selectedButtonStyle = new GUIStyle(buttonStyle);
            selectedButtonStyle.normal.background = MakeTexture(new Color(0.20f, 0.42f, 0.58f, 1f));
            selectedButtonStyle.normal.textColor = Color.white;
            selectedButtonStyle.hover.background = MakeTexture(new Color(0.25f, 0.50f, 0.68f, 1f));
            selectedButtonStyle.hover.textColor = Color.white;
            selectedButtonStyle.active.background = MakeTexture(new Color(0.15f, 0.34f, 0.48f, 1f));

            topBarMetricStyle = new GUIStyle(buttonStyle);
            topBarMetricStyle.alignment = TextAnchor.MiddleCenter;
            topBarMetricStyle.normal.textColor = new Color(0.42f, 0.70f, 0.92f);
            topBarMetricStyle.hover.textColor = topBarMetricStyle.normal.textColor;
            topBarMetricStyle.active.textColor = topBarMetricStyle.normal.textColor;

            stylesInitialized = true;
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
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

        private static bool IsOffSale(CosmeticsController.CosmeticItem item)
        {
            return item.cost <= 0;
        }

        private int GetCartTotal()
        {
            CosmeticsController cosmeticsController = CosmeticsController.instance;
            if (cosmeticsController == null || cosmeticsController.currentCart == null)
            {
                return 0;
            }

            int total = 0;
            foreach (CosmeticsController.CosmeticItem item in cosmeticsController.currentCart)
            {
                total += item.cost;
            }

            return total;
        }

        private void StartCartPurchase(CosmeticsController cosmeticsController)
        {
            cartPurchaseQueue.Clear();
            foreach (CosmeticsController.CosmeticItem item in cosmeticsController.currentCart)
            {
                cartPurchaseQueue.Enqueue(item);
            }

            cartPurchaseInProgress = cartPurchaseQueue.Count > 0;
            cartPurchaseWaitingForResult = false;
            cartPurchaseStarted = false;
            BeginNextCartPurchase(cosmeticsController);
        }

        private void ProcessCartPurchaseQueue(CosmeticsController cosmeticsController)
        {
            if (!cartPurchaseInProgress || !cartPurchaseWaitingForResult)
            {
                return;
            }

            string stage = cosmeticsController.currentPurchaseItemStage.ToString();
            if (stage != cartPurchaseInitialStage)
            {
                cartPurchaseStarted = true;
            }

            if (!cartPurchaseStarted)
            {
                return;
            }

            if (stage == "Failure")
            {
                cartPurchaseQueue.Clear();
                cartPurchaseInProgress = false;
                cartPurchaseWaitingForResult = false;
                cartMessage = "Cart purchase stopped because an item could not be purchased.";
                return;
            }

            if (stage == "Success")
            {
                cartPurchaseWaitingForResult = false;
                BeginNextCartPurchase(cosmeticsController);
            }
        }

        private void BeginNextCartPurchase(CosmeticsController cosmeticsController)
        {
            if (cartPurchaseQueue.Count == 0)
            {
                cartPurchaseInProgress = false;
                cartPurchaseWaitingForResult = false;
                cartMessage = "Finished purchasing the cart.";
                return;
            }

            CosmeticsController.CosmeticItem item = cartPurchaseQueue.Dequeue();
            cartPurchaseInitialStage = cosmeticsController.currentPurchaseItemStage.ToString();
            cartPurchaseStarted = false;
            cosmeticsController.itemToBuy = item;
            cosmeticsController.PurchaseItem();
            cartPurchaseWaitingForResult = true;
            cartMessage = "Purchasing " + GetDisplayName(item) + "...";
        }

        private static int ParsePrice(string priceText)
        {
            return int.TryParse(priceText, out int price) && price >= 0 ? price : -1;
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
