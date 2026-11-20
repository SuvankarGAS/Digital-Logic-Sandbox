using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.GateLibrary.Packaging;
using DigitalLogicSandbox.UI.Panels;
using NUnit.Framework;
using DigitalLogicSandbox.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// The gate library panel in the running app: ranked search, keyboard placement, pins and recents, collapsible
    /// categories, collapse and resize, the hover preview card and custom gate management from the library.
    /// </summary>
    public sealed class LibraryPanelTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private LibraryPanel _library;
        private string _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            _library = _app.Shell.Library;
            _library.Animate = false;
            _root = Path.Combine(Application.temporaryCachePath, "LibraryPanelTest-" + Guid.NewGuid().ToString("N"));
            _app.Projects.ProjectsDirectory = Path.Combine(_root, "Projects");
            _app.Projects.AutosaveDirectory = Path.Combine(_root, "Autosave");
            _app.Projects.RememberLastProject = false;
            _app.Projects.AutosaveIntervalSeconds = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null && Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static PinRef Pin(NodeId boundary)
        {
            return new PinRef(boundary, BoundarySignatures.Pin);
        }

        private CircuitDefinition PackageNand(string name, string category = null)
        {
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, 1));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, -1));
            NodeId and = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(-1, 0));
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(3, 0));
            NodeId output = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(7, 0));
            _workspace.Connect(Pin(a), new PinRef(and, AndGate.InputA));
            _workspace.Connect(Pin(b), new PinRef(and, AndGate.InputB));
            _workspace.Connect(new PinRef(and, AndGate.Output), new PinRef(not, NotGate.Input));
            _workspace.Connect(new PinRef(not, NotGate.Output), Pin(output));
            PackagingSpec spec = _workspace.CreatePackagingSpec(selectionOnly: false);
            spec.Name = name;
            spec.Category = category;
            PackagingResult result = _workspace.PackageCustomGate(spec, true);
            Assert.That(result.Succeeded, Is.True, result.Report.ToString());
            return result.Definition;
        }

        private LibraryItemView Find(string title)
        {
            for (int i = 0; i < _library.Items.Count; i++)
            {
                if (_library.Items[i].Title == title)
                {
                    return _library.Items[i];
                }
            }

            return null;
        }

        private LibraryItemView Find(ComponentRef component)
        {
            for (int i = 0; i < _library.Items.Count; i++)
            {
                if (_library.Items[i].Request.Component == component)
                {
                    return _library.Items[i];
                }
            }

            return null;
        }

        [UnityTest]
        public IEnumerator Search_RanksNamePrefixFirst_HighlightsMatch_AndClears()
        {
            _library.SetFilter("in");
            yield return null;

            Assert.That(_library.NavigableItems.Count, Is.GreaterThan(1));
            Assert.That(_library.HighlightedItem.Title, Is.EqualTo("Input"),
                "The best match (name prefix) is highlighted, not AND's 'built-in' description match.");

            _library.SetFilter("bit");
            Assert.That(Find("Tri-state (1-bit)").transform.GetSiblingIndex(),
                Is.LessThan(Find("Bus Input").transform.GetSiblingIndex()),
                "A name match moves above description matches ('4-bit switches') within its section.");
            _library.SetFilter("in");
            Assert.That(Find("Input").DisplayedName, Does.Contain("<u>"), "The matching text is highlighted.");
            Assert.That(_library.ClearSearchButton.gameObject.activeSelf, Is.True);

            _library.SetFilter("zzqq");
            Assert.That(_library.IsShowingNoResults, Is.True);
            Assert.That(_library.NavigableItems, Is.Empty);

            _library.ClearSearch();
            yield return null;
            Assert.That(_library.IsShowingNoResults, Is.False);
            Assert.That(_library.ClearSearchButton.gameObject.activeSelf, Is.False);
            Assert.That(Find("Input").DisplayedName, Is.EqualTo("Input"));
            Assert.That(Find("AND").gameObject.activeInHierarchy, Is.True);
            Assert.That(_library.HighlightedItem, Is.Null);
            Assert.That(Find("Bus Input").transform.GetSiblingIndex(),
                Is.LessThan(Find("Tri-state (1-bit)").transform.GetSiblingIndex()), "Clearing restores the home order.");
        }

        [UnityTest]
        public IEnumerator Search_MatchesPinNames()
        {
            _library.SetFilter("dp");
            yield return null;
            Assert.That(Find("7-Segment").gameObject.activeSelf, Is.True, "The seven-segment display has a 'dp' pin.");
        }

        [UnityTest]
        public IEnumerator Keyboard_ArrowsMoveHighlight_EnterArmsPlacement()
        {
            _library.SetFilter("in");
            yield return null;
            LibraryItemView first = _library.HighlightedItem;
            _library.MoveHighlight(1);
            Assert.That(_library.HighlightedItem, Is.Not.SameAs(first));
            Assert.That(_library.HighlightedItem.IsHighlighted, Is.True);
            Assert.That(first.IsHighlighted, Is.False);
            _library.MoveHighlight(-1);
            Assert.That(_library.HighlightedItem, Is.SameAs(first));
            _library.MoveHighlight(-100);
            Assert.That(_library.HighlightedItem, Is.SameAs(_library.NavigableItems[0]), "The highlight is clamped.");
            _library.MoveHighlight(1);
            while (_library.HighlightedItem != first)
            {
                _library.MoveHighlight(1);
            }

            Assert.That(_library.PlaceHighlighted(), Is.True);
            Assert.That(_workspace.IsPlacing, Is.True);
            Assert.That(_workspace.Placement.Value.Component, Is.EqualTo(ComponentRef.InputBoundary));
            Assert.That(first.IsArmed, Is.True, "The entry being placed shows the armed state.");

            _workspace.CancelPlacement();
            Assert.That(first.IsArmed, Is.False);
            _app.Shell.GatePreview.Release(null);
        }

        [UnityTest]
        public IEnumerator Favorites_LiveInTheirOwnTab_WithoutChangingItems()
        {
            int count = _library.Items.Count;
            LibraryItemView and = Find("AND");
            Assert.That(_library.CurrentTab, Is.EqualTo(LibraryTab.All));

            _library.SetPinned(and.Request, true);
            yield return null;

            Assert.That(_library.PinnedItems.Count, Is.EqualTo(1));
            Assert.That(_library.PinnedItems[0].Title, Is.EqualTo("AND"));
            Assert.That(_library.PinnedItems[0].gameObject.activeInHierarchy, Is.False,
                "Favorites are not shown above the main list.");
            Assert.That(_library.Items.Count, Is.EqualTo(count), "Shortcut rows are not library entries.");
            Assert.That(and.IsPinned, Is.True);

            _library.SetTab(LibraryTab.Favorites);
            Assert.That(_library.PinnedItems[0].gameObject.activeInHierarchy, Is.True);
            Assert.That(and.gameObject.activeInHierarchy, Is.False, "The All list hides on the Favorites tab.");
            Assert.That(_library.IsShowingEmptyTab, Is.False);
            Assert.That(_library.Preferences.LibraryTab, Is.EqualTo((int)LibraryTab.Favorites));

            _library.SetPinned(Find("NOT").Request, true);
            _library.SetFilter("not");
            Assert.That(_library.NavigableItems.Count, Is.EqualTo(1), "Search works inside the Favorites tab.");
            Assert.That(_library.NavigableItems[0].IsShortcut, Is.True);
            Assert.That(_library.NavigableItems[0].Title, Is.EqualTo("NOT"));
            _library.SetFilter(string.Empty);

            _library.SetPinned(and.Request, false);
            _library.SetPinned(Find("NOT").Request, false);
            Assert.That(_library.PinnedItems, Is.Empty);
            Assert.That(_library.IsShowingEmptyTab, Is.True, "An empty Favorites tab explains how to add parts.");

            _library.SetTab(LibraryTab.All);
            Assert.That(and.gameObject.activeInHierarchy, Is.True);
        }

        [UnityTest]
        public IEnumerator Tabs_ReactToHover()
        {
            yield return null;
            var favorites = FindDeep(_library.transform, "Tab Favorites").GetComponent<LibraryTabView>();
            var all = FindDeep(_library.transform, "Tab All").GetComponent<LibraryTabView>();
            Assert.That(all.IsActive, Is.True);
            Assert.That(favorites.Tile.color.a, Is.EqualTo(0f), "An inactive tab rests without a tile.");

            var data = new PointerEventData(EventSystem.current);
            favorites.OnPointerEnter(data);
            DG.Tweening.DOTween.Complete(favorites.Tile);
            Assert.That(favorites.IsHovered, Is.True);
            Assert.That(favorites.Tile.color.a, Is.GreaterThan(0f), "Hovering shows a soft tile.");

            favorites.OnPointerExit(data);
            DG.Tweening.DOTween.Complete(favorites.Tile);
            Assert.That(favorites.Tile.color.a, Is.EqualTo(0f));
            Assert.That(all.Tile.color.a, Is.GreaterThan(0f), "The active tab keeps its raised tile.");
        }

        [UnityTest]
        public IEnumerator RecentTab_ShowsPlacedParts_AndClears()
        {
            LibraryItemView not = Find("NOT");
            _workspace.BeginPlacement(not.Request, fromDrag: false);
            _workspace.CommitPlacement(Vector2.zero, keepPlacing: false);
            _library.SetTab(LibraryTab.Recent);
            yield return null;

            Assert.That(_library.RecentItems.Count, Is.EqualTo(1));
            Assert.That(_library.RecentItems[0].gameObject.activeInHierarchy, Is.True);
            Assert.That(not.gameObject.activeInHierarchy, Is.False);

            Transform clear = FindDeep(_library.transform, "Recent Bar").Find("Clear");
            clear.GetComponent<Button>().onClick.Invoke();
            Assert.That(_library.RecentItems, Is.Empty);
            Assert.That(_library.IsShowingEmptyTab, Is.True);
        }

        [UnityTest]
        public IEnumerator Rows_FollowTheTreeIndentation()
        {
            CircuitDefinition first = PackageNand("Alpha", "Logic");
            PackageNand("Beta", "Arithmetic");
            LibraryItemView and = Find("AND");
            _library.SetPinned(and.Request, true);
            yield return null;

            LibraryItemView category = Find(ComponentRef.Custom(first.Reference));
            LibraryItemView favorite = _library.PinnedItems[0];
            Assert.That(favorite.TileLeft, Is.LessThan(and.TileLeft), "Tab lists have no tree indent.");
            Assert.That(and.TileLeft, Is.LessThan(category.TileLeft), "Category rows sit one level deeper.");

            var sectionTitle = (RectTransform)FindDeep(_library.transform, "Section Gates").Find("Title");
            Assert.That(sectionTitle.offsetMin.x, Is.EqualTo(and.TileLeft).Within(0.01f),
                "Rows start under their section title.");
            var categoryTitle = (RectTransform)FindDeep(_library.transform, "Section Logic").Find("Title");
            Assert.That(categoryTitle.offsetMin.x, Is.EqualTo(category.TileLeft).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator Wheel_ScrollsOneStepPerNotch_AndGlides()
        {
            _workspace.LoadExampleGates();
            yield return null;
            yield return null;
            var scroll = (SmoothScrollRect)_library.Scroll;
            Assert.That(scroll.MaxOffset, Is.GreaterThan(scroll.StepPixels * 2f), "The example gates make the list scroll.");
            scroll.ScrollTo(0f, animate: false);

            var data = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, -6f) };
            scroll.OnScroll(data);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(scroll.StepPixels).Within(0.01f),
                "One notch (6 units from the Input System module) moves one step, not 6 x sensitivity.");

            scroll.Animate = true;
            scroll.OnScroll(data);
            Assert.That(scroll.TargetOffset, Is.EqualTo(scroll.StepPixels * 2f).Within(0.01f));
            Assert.That(scroll.content.anchoredPosition.y, Is.LessThan(scroll.StepPixels * 2f - 0.5f), "It glides.");
            for (int i = 0; i < 60 && scroll.content.anchoredPosition.y < scroll.StepPixels * 2f - 0.01f; i++)
            {
                yield return null;
            }

            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(scroll.StepPixels * 2f).Within(0.01f));

            scroll.Animate = false;
            data.scrollDelta = new Vector2(0f, 600f);
            scroll.OnScroll(data);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0f), "Scrolling is clamped at the top.");

            data.scrollDelta = new Vector2(0f, -600f);
            scroll.OnScroll(data);
            Assert.That(scroll.content.anchoredPosition.y,
                Is.EqualTo(Mathf.Min(scroll.MaxOffset, scroll.StepPixels * SmoothScrollRect.MaxNotchesPerEvent)).Within(0.01f),
                "A huge wheel delta moves at most a few notches.");
        }

        [UnityTest]
        public IEnumerator Escape_HidesKeyboardPreview()
        {
            GatePreviewCard card = _app.Shell.GatePreview;
            _library.FocusSearch();
            yield return null;
            _library.SetFilter("in");
            _library.MoveHighlight(1);
            Assert.That(card.gameObject.activeSelf, Is.True, "Arrow keys show the highlighted part's card.");

            Assert.That(_library.HandleEscape(), Is.True, "The first Escape clears the search text.");
            Assert.That(card.gameObject.activeSelf, Is.False);
            Assert.That(_library.HighlightedItem, Is.Null);

            _library.SetFilter("not");
            _library.MoveHighlight(1);
            Assert.That(card.gameObject.activeSelf, Is.True);
            _library.SearchField.SetTextWithoutNotify(string.Empty);
            Assert.That(_library.HandleEscape(), Is.False, "With no text, Escape just leaves the field.");
            Assert.That(card.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator Wheel_OverBusFields_StillScrollsTheList()
        {
            _workspace.LoadExampleGates();
            FindDeep(_library.transform, "Section Buses").GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;
            var scroll = (SmoothScrollRect)_library.Scroll;
            scroll.ScrollTo(0f, animate: false);

            var data = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, -6f) };
            ExecuteEvents.Execute(_library.BusWidthField.gameObject, data, ExecuteEvents.scrollHandler);

            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator CommittedPlacement_IsRecordedInRecent()
        {
            LibraryItemView not = Find("NOT");
            _workspace.BeginPlacement(not.Request, fromDrag: false);
            NodeId placed = _workspace.CommitPlacement(Vector2.zero, keepPlacing: false);
            yield return null;

            Assert.That(placed.IsValid, Is.True);
            Assert.That(_library.RecentItems.Count, Is.EqualTo(1));
            Assert.That(_library.RecentItems[0].Title, Is.EqualTo("NOT"));
            Assert.That(_library.Preferences.Recent[0], Is.EqualTo(LibraryPanel.KeyOf(not.Request)));
        }

        [UnityTest]
        public IEnumerator Categories_AreCollapsible_AndSearchExpandsThem()
        {
            CircuitDefinition first = PackageNand("Alpha", "Logic");
            CircuitDefinition second = PackageNand("Beta", "Arithmetic");
            yield return null;

            LibraryItemView alpha = Find(ComponentRef.Custom(first.Reference));
            Assert.That(alpha, Is.Not.Null);
            Assert.That(alpha.gameObject.activeInHierarchy, Is.True);
            Transform header = FindDeep(_library.transform, "Section Logic");
            Assert.That(header, Is.Not.Null, "Each category has its own header.");

            header.GetComponent<Button>().onClick.Invoke();
            Assert.That(alpha.gameObject.activeInHierarchy, Is.False, "Collapsing a category hides its gates.");
            Assert.That(Find(ComponentRef.Custom(second.Reference)).gameObject.activeInHierarchy, Is.True);
            Assert.That(_library.Preferences.IsSectionExpanded("category:Logic", true), Is.False);

            _library.SetFilter("alpha");
            Assert.That(alpha.gameObject.activeInHierarchy, Is.True, "Searching expands matching categories.");
            _library.SetFilter(string.Empty);
            Assert.That(alpha.gameObject.activeInHierarchy, Is.False, "The user's choice returns after searching.");
        }

        [UnityTest]
        public IEnumerator CollapseAndResize_ChangeWidthAndWorkspaceArea()
        {
            yield return null;
            Rect wide = _app.Shell.GetWorkspaceScreenRect();

            _library.SetCollapsed(true);
            yield return null;
            Assert.That(_library.CurrentWidth, Is.EqualTo(LibraryPanel.CollapsedWidth));
            Assert.That(_library.Preferences.LibraryCollapsed, Is.True);
            Rect collapsed = _app.Shell.GetWorkspaceScreenRect();
            Assert.That(collapsed.xMin, Is.LessThan(wide.xMin), "The workspace gains the room the library gave up.");

            _library.SetCollapsed(false);
            _library.SetWidth(360f);
            yield return null;
            Assert.That(_library.CurrentWidth, Is.EqualTo(360f));
            Assert.That(_library.Preferences.LibraryWidth, Is.EqualTo(360f));
            Assert.That(_app.Shell.GetWorkspaceScreenRect().xMin, Is.GreaterThan(wide.xMin));

            _library.SetWidth(5000f);
            Assert.That(_library.ExpandedWidth, Is.EqualTo(DigitalLogicSandbox.UI.UiPreferences.MaxLibraryWidth));
        }

        [UnityTest]
        public IEnumerator FocusSearch_ExpandsCollapsedPanel()
        {
            _library.SetCollapsed(true);
            _library.FocusSearch();
            yield return null;
            Assert.That(_library.IsCollapsed, Is.False);
        }

        [UnityTest]
        public IEnumerator PreviewCard_ShowsPinsAndTruthTable()
        {
            CircuitDefinition nand = PackageNand("NAND");
            yield return null;
            GatePreviewCard card = _app.Shell.GatePreview;

            card.ShowNow(Find(ComponentRef.Custom(nand.Reference)), (RectTransform)_library.transform);
            Assert.That(card.TitleText, Is.EqualTo("NAND"));
            Assert.That(card.PinNames.Count, Is.EqualTo(3), "Two inputs and one output.");
            Assert.That(card.TableRows, Is.EqualTo(4), "NAND has a four-row truth table.");

            card.ShowNow(Find("NOT"), (RectTransform)_library.transform);
            Assert.That(card.TableRows, Is.EqualTo(2));

            card.ShowNow(Find("Input"), (RectTransform)_library.transform);
            Assert.That(card.TableRows, Is.Zero, "Utilities have no truth table.");

            card.Release(null);
            Assert.That(card.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator PreviewCard_FitsSixteenRowTable()
        {
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-8, 3));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-8, 1));
            NodeId c = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-8, -1));
            NodeId d = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-8, -3));
            NodeId and1 = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(-3, 2));
            NodeId and2 = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(-3, -2));
            NodeId and3 = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(2, 0));
            NodeId y = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(6, 1));
            NodeId z = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(6, -1));
            _workspace.Connect(Pin(a), new PinRef(and1, AndGate.InputA));
            _workspace.Connect(Pin(b), new PinRef(and1, AndGate.InputB));
            _workspace.Connect(Pin(c), new PinRef(and2, AndGate.InputA));
            _workspace.Connect(Pin(d), new PinRef(and2, AndGate.InputB));
            _workspace.Connect(new PinRef(and1, AndGate.Output), new PinRef(and3, AndGate.InputA));
            _workspace.Connect(new PinRef(and2, AndGate.Output), new PinRef(and3, AndGate.InputB));
            _workspace.Connect(new PinRef(and3, AndGate.Output), Pin(y));
            _workspace.Connect(new PinRef(and1, AndGate.Output), Pin(z));
            PackagingSpec spec = _workspace.CreatePackagingSpec(selectionOnly: false);
            spec.Name = "AND4";
            PackagingResult result = _workspace.PackageCustomGate(spec, true);
            Assert.That(result.Succeeded, Is.True, result.Report.ToString());
            yield return null;

            GatePreviewCard card = _app.Shell.GatePreview;
            card.ShowNow(Find(ComponentRef.Custom(result.Definition.Reference)), (RectTransform)_library.transform);
            yield return null;

            Assert.That(card.TableRows, Is.EqualTo(16));
            Assert.That(card.TableArea.rect.width, Is.LessThanOrEqualTo(GatePreviewCard.TableInnerWidth + 20.01f));
            var cardRect = (RectTransform)card.transform;
            var canvas = (RectTransform)cardRect.parent;
            Assert.That(cardRect.rect.height, Is.LessThanOrEqualTo(canvas.rect.height - 15f), "The card fits the screen.");
            Assert.That(cardRect.anchoredPosition.y, Is.LessThanOrEqualTo(canvas.rect.height));
            Assert.That(cardRect.anchoredPosition.y - cardRect.rect.height, Is.GreaterThanOrEqualTo(0f));
            if (card.ShownTableRows == card.TableRows)
            {
                Assert.That(card.TableSplit, Is.True, "Sixteen rows are drawn as two blocks of eight.");
            }

            card.Release(null);
        }

        [UnityTest]
        public IEnumerator Manage_RenameDuplicateDelete_AreUndoable()
        {
            CircuitDefinition nand = PackageNand("NAND");
            yield return null;
            ComponentRef component = ComponentRef.Custom(nand.Reference);

            Assert.That(_workspace.SetCustomGateDetails(nand.Id, "Not And", null, "Logic", "NA"), Is.True);
            Assert.That(Find(component).Title, Is.EqualTo("Not And"));
            Assert.That(Find(component).Spec.Category, Is.EqualTo("Logic"));
            _workspace.Undo();
            Assert.That(Find(component).Title, Is.EqualTo("NAND"));

            int before = _library.Items.Count;
            CircuitDefinition copy = _workspace.DuplicateCustomGate(nand.Id);
            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.Id, Is.Not.EqualTo(nand.Id));
            Assert.That(copy.Name, Is.EqualTo("NAND copy"));
            Assert.That(_library.Items.Count, Is.EqualTo(before + 1));
            _workspace.Undo();
            Assert.That(_library.Items.Count, Is.EqualTo(before));

            NodeId instance = _workspace.PlaceNode(component, new Vector2(0, 8));
            Assert.That(instance.IsValid, Is.True);
            Assert.That(_workspace.CanDeleteCustomGate(nand.Id, out string reason), Is.False);
            Assert.That(reason, Does.Contain("main circuit"));
            Assert.That(_workspace.DeleteCustomGate(nand.Id), Is.False);
            Assert.That(Find(component), Is.Not.Null);

            _workspace.Undo(); // remove the instance
            Assert.That(_workspace.DeleteCustomGate(nand.Id), Is.True);
            Assert.That(Find(component), Is.Null);
            Assert.That(_app.Library.Contains(nand.Reference), Is.False);

            _workspace.Undo();
            Assert.That(_app.Library.Contains(nand.Reference), Is.True);
            Assert.That(Find(component), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator EntryMenu_OffersManagementForCustomGates()
        {
            CircuitDefinition nand = PackageNand("NAND");
            yield return null;
            var shell = _app.Shell;

            shell.ShowLibraryEntryMenu(Find(ComponentRef.Custom(nand.Reference)).Request, new Vector2(200, 400));
            IReadOnlyList<string> labels = shell.ContextMenu.Labels;
            Assert.That(labels, Does.Contain("Place"));
            Assert.That(labels, Does.Contain("Add to favorites"));
            Assert.That(labels, Does.Contain("Rename…"));
            Assert.That(labels, Does.Contain("Duplicate"));
            Assert.That(labels, Does.Contain("Delete…"));

            Assert.That(shell.ContextMenu.Invoke("Rename…"), Is.True);
            Assert.That(shell.GateDetails.IsOpen, Is.True);
            shell.GateDetails.NameField.text = "Renamed";
            Assert.That(shell.GateDetails.Confirm(), Is.True);
            Assert.That(Find(ComponentRef.Custom(nand.Reference)).Title, Is.EqualTo("Renamed"));

            shell.ShowLibraryEntryMenu(Find("AND").Request, new Vector2(200, 400));
            Assert.That(shell.ContextMenu.Labels, Does.Not.Contain("Delete…"), "Built-in gates cannot be managed.");
            shell.ContextMenu.Close();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
