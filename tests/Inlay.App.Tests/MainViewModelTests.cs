using Inlay.App.Services;
using Inlay.App.ViewModels;
using Inlay.Core;

namespace Inlay.App.Tests;

/// <summary>
/// What the panel does with names and selections, on a real archive in a temporary folder. The view relies on it:
/// a click elsewhere confirms a name only after the click is done, and the selection must survive the reload.
/// </summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    // The recent places of the test, outside the archive and never those of the PC.
    private readonly RecentDestinations _recent;

    public MainViewModelTests()
    {
        _recent = RecentDestinations.Load(_root + "-recenti.json");
        AddFile("Lavoro/Documenti/a.txt");
        AddFile("Lavoro/Documenti/b.txt");
        Directory.CreateDirectory(Path.Combine(_root, "Lavoro", "Archivio"));
        AddFile("Personale/Casa/bollette.csv");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        File.Delete(_root + "-recenti.json");
    }

    [Fact]
    public void RenamingTheSelectedItem_KeepsItSelectedUnderTheNewName() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var restored = 0;
        viewModel.SelectionRestored += (_, _) => restored++;
        var item = Item(viewModel, "a.txt");
        item.IsSelected = true;

        viewModel.BeginRename(item);
        item.EditName = "c.txt";
        Assert.Null(viewModel.CommitRename(item));

        Assert.True(File.Exists(Path.Combine(_root, "Lavoro", "Documenti", "c.txt")));
        Assert.Equal(["c.txt"], viewModel.SelectedItems().Select(selected => selected.Name));
        Assert.Equal(1, restored);
    });

    [Fact]
    public void ANameConfirmedByClickingAnotherItem_LeavesThatItemSelected() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var renamed = Item(viewModel, "a.txt");
        renamed.IsSelected = true;
        viewModel.BeginRename(renamed);
        renamed.EditName = "c.txt";

        // The click selects the other item first; the name is confirmed right after it.
        renamed.IsSelected = false;
        Item(viewModel, "b.txt").IsSelected = true;
        Assert.Null(viewModel.CommitRename(renamed));

        Assert.Equal(["b.txt"], viewModel.SelectedItems().Select(selected => selected.Name));
        Assert.False(Item(viewModel, "c.txt").IsEditing);
    });

    [Fact]
    public void ChoosingAnotherTopic_DoesNotThrowAwayTheNameBeingTyped() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var item = Item(viewModel, "a.txt");
        viewModel.BeginRename(item);
        item.EditName = "c.txt";

        viewModel.SelectTopicCommand.Execute(viewModel.Topics.Single(topic => topic.Name == "Personale"));
        Assert.Null(viewModel.CommitRename(item));

        Assert.True(File.Exists(Path.Combine(_root, "Lavoro", "Documenti", "c.txt")));
        Assert.Equal("Personale", viewModel.BoardTitle);
    });

    [Fact]
    public void HidingThePanel_KeepsTheNameBeingTyped() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var item = Item(viewModel, "a.txt");
        viewModel.BeginRename(item);
        item.EditName = "c.txt";

        viewModel.OnPanelHidden();

        Assert.True(File.Exists(Path.Combine(_root, "Lavoro", "Documenti", "c.txt")));
        Assert.DoesNotContain(viewModel.Tiles.SelectMany(tile => tile.Items), candidate => candidate.IsEditing);
    });

    [Fact]
    public void AnUnusableName_LeavesTheFieldOpen_WithTheReason() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var item = Item(viewModel, "a.txt");
        viewModel.BeginRename(item);
        item.EditName = "b.txt";

        Assert.Contains("b.txt", viewModel.CommitRename(item));
        Assert.True(item.IsEditing);
        Assert.True(File.Exists(Path.Combine(_root, "Lavoro", "Documenti", "a.txt")));
    });

    [Fact]
    public void AShortcut_IsRenamedWithoutSeeingItsExtension() => StaThread.Run(() =>
    {
        AddFile("Lavoro/Documenti/Portale.url");
        using var viewModel = Open();
        var item = Item(viewModel, "Portale");

        viewModel.BeginRename(item);
        Assert.Equal("Portale", item.EditName);
        Assert.Equal("Portale".Length, item.EditSelectionLength);
        item.EditName = "Portale clienti";
        Assert.Null(viewModel.CommitRename(item));

        Assert.True(File.Exists(Path.Combine(_root, "Lavoro", "Documenti", "Portale clienti.url")));
    });

    [Fact]
    public void MovedFiles_AreSelectedWhereTheyLanded() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var documents = Path.Combine(_root, "Lavoro", "Documenti");
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        Item(viewModel, "a.txt").IsSelected = true;

        viewModel.Transfer([Path.Combine(documents, "a.txt"), Path.Combine(documents, "b.txt")], archive, TransferKind.Move);

        var selected = viewModel.SelectedItems();
        Assert.Equal(["a.txt", "b.txt"], selected.Select(item => item.Name));
        Assert.All(selected, item => Assert.Equal(archive, Path.GetDirectoryName(item.Item.FullPath)));
    });

    [Fact]
    public void ANewFolder_IsTheOnlySelection_WithItsNameReadyToType() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        Item(viewModel, "a.txt").IsSelected = true;

        viewModel.NewFolder(viewModel.Tiles.Single(tile => tile.Title == "Documenti"));

        var folder = Assert.Single(viewModel.SelectedItems());
        Assert.Equal("New folder", folder.Name);
        Assert.True(folder.IsEditing);
        Assert.True(Directory.Exists(Path.Combine(_root, "Lavoro", "Documenti", "New folder")));
    });

    [Fact]
    public void ANewFolderInGeneral_IsOneOfItsRows_WithItsNameReadyToType() => StaThread.Run(() =>
    {
        AddFile("Lavoro/Promemoria.txt");
        using var viewModel = Open();

        viewModel.NewFolder(viewModel.Tiles.Single(tile => tile.IsGeneral));

        var folder = Assert.Single(viewModel.SelectedItems());
        Assert.Equal("New folder", folder.Name);
        Assert.True(folder.IsEditing);
        Assert.True(folder.CanBecomeTile);
        Assert.Equal(["New folder", "Promemoria.txt"], viewModel.Tiles.Single(tile => tile.IsGeneral).Items.Select(item => item.Name));
        Assert.Equal(["General", "Archivio", "Documenti"], viewModel.Tiles.Select(tile => tile.Title));
    });

    [Fact]
    public void OnTheCanvas_ANewFolderInGeneral_IsARowOfItsZone_NotOfTheTopicInTheSidebar() => StaThread.Run(() =>
    {
        AddFile("Personale/Promemoria.txt");
        using var viewModel = Open();
        Assert.Equal("Lavoro", viewModel.SelectedTopic?.Name);
        viewModel.IsCanvasShown = true;
        var general = viewModel.Canvas.Zones.Single(zone => zone.Key == "Personale").Tiles.Single(tile => tile.IsGeneral);

        viewModel.NewFolder(general);

        var created = Path.Combine(_root, "Personale", "New folder");
        Assert.True(Directory.Exists(created));
        Assert.False(Directory.Exists(Path.Combine(_root, "Lavoro", "New folder")));
        var row = Assert.Single(viewModel.SelectedItems());
        Assert.Equal(created, row.Item.FullPath);
        Assert.True(row.IsEditing);
        Assert.Contains(row, viewModel.Canvas.Zones.Single(zone => zone.Key == "Personale").Tiles.Single(tile => tile.IsGeneral).Items);
    });

    // ---- Folders arriving in a topic: rows of «General» ----

    [Fact]
    public void AFolderDroppedOnATopic_IsARowOfItsGeneral_AndCtrlZPutsItBackATile() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var documents = Path.Combine(_root, "Lavoro", "Documenti");

        viewModel.Transfer([documents], Path.Combine(_root, "Personale"), TransferKind.Move);

        viewModel.SelectTopicByPath("Personale");
        Assert.Equal(["General", "Casa"], viewModel.Tiles.Select(tile => tile.Title));
        var row = Assert.Single(viewModel.Tiles[0].Items);
        Assert.Equal("Documenti", row.Name);
        Assert.True(row.CanBecomeTile);

        viewModel.Undo();

        Assert.True(Directory.Exists(documents));
        var metadata = MetadataStore.Load(_root);
        Assert.False(metadata.IsItem("Lavoro/Documenti"));
        Assert.DoesNotContain("Personale/Documenti", metadata.Folders.Keys);
        viewModel.SelectTopicByPath("Lavoro");
        Assert.Equal(["Archivio", "Documenti"], viewModel.Tiles.Select(tile => tile.Title));
    });

    [Fact]
    public void AFolderCopiedFromOutside_IsARowOfTheTopicItLandsIn() => StaThread.Run(() =>
    {
        var outside = Directory.CreateDirectory(_root + "-fuori").FullName;
        try
        {
            var photos = Directory.CreateDirectory(Path.Combine(outside, "Foto")).FullName;
            File.WriteAllText(Path.Combine(photos, "mare.jpg"), "");
            using var viewModel = Open();

            viewModel.Transfer([photos], Path.Combine(_root, "Lavoro"), TransferKind.Copy);

            Assert.Equal(["Foto"], viewModel.Tiles.Single(tile => tile.IsGeneral).Items.Select(item => item.Name));
            Assert.Equal(["General", "Archivio", "Documenti"], viewModel.Tiles.Select(tile => tile.Title));
            Assert.True(MetadataStore.Load(_root).IsItem("Lavoro/Foto"));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    });

    [Fact]
    public void ATileDraggedByItsTitle_StaysATile_AndAFolderDroppedOnATile_IsOneOfItsItems() => StaThread.Run(() =>
    {
        using var viewModel = Open();

        viewModel.Transfer([Path.Combine(_root, "Lavoro", "Documenti")], Path.Combine(_root, "Personale"), TransferKind.Move, foldersAsRows: false);
        viewModel.Transfer([Path.Combine(_root, "Lavoro", "Archivio")], Path.Combine(_root, "Personale", "Casa"), TransferKind.Move);

        viewModel.SelectTopicByPath("Personale");
        Assert.Equal(["Casa", "Documenti"], viewModel.Tiles.Select(tile => tile.Title));
        Assert.Equal(["Archivio", "bollette.csv"], viewModel.Tiles[0].Items.Select(item => item.Name));
        Assert.DoesNotContain(MetadataStore.Load(_root).Folders.Values, folder => folder.Kind == ArchiveMetadata.ItemKind);
    });

    [Fact]
    public void ARowPromotedToTile_AndATilePutInGeneral_TurnBackWithCtrlZ() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.Transfer([Path.Combine(_root, "Lavoro", "Archivio")], Path.Combine(_root, "Personale"), TransferKind.Move);
        viewModel.SelectTopicByPath("Personale");

        viewModel.PromoteItem(Assert.Single(viewModel.Tiles.Single(tile => tile.IsGeneral).Items));

        Assert.Equal(["Archivio", "Casa"], viewModel.Tiles.Select(tile => tile.Title));
        Assert.Equal("Undo “Unfold as tile”", viewModel.UndoLabel);

        viewModel.DemoteTile(viewModel.Tiles.Single(tile => tile.Title == "Casa"));

        Assert.Equal(["General", "Archivio"], viewModel.Tiles.Select(tile => tile.Title));
        Assert.Equal(["Casa"], viewModel.SelectedItems().Select(item => item.Name));
        Assert.Equal("Undo “Fold into General”", viewModel.UndoLabel);

        viewModel.Undo();
        Assert.Equal(["Archivio", "Casa"], viewModel.Tiles.Select(tile => tile.Title));

        viewModel.Undo();
        Assert.Equal(["General", "Casa"], viewModel.Tiles.Select(tile => tile.Title));
        Assert.Equal(["Archivio"], viewModel.Tiles[0].Items.Select(item => item.Name));
    });

    [Fact]
    public void AnArchiveOfQuiver_KeepsItsTopics_AndItsMetadataBecomesInlays_WhenOpened() => StaThread.Run(() =>
    {
        // Made when Inlay was called Quiver: the metadata in .quiver.
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Archivio", true);
        MetadataStore.Save(_root, metadata);
        Directory.Move(Path.Combine(_root, ArchiveLayout.MetadataFolderName), Path.Combine(_root, ArchiveLayout.OldMetadataFolderName));

        using var viewModel = Open();

        Assert.Equal(["Archivio"], viewModel.Topics.Single(topic => topic.Name == "Lavoro").Children.Select(child => child.Name));
        Assert.True(Directory.Exists(Path.Combine(_root, ArchiveLayout.MetadataFolderName)));
        Assert.False(Directory.Exists(Path.Combine(_root, ArchiveLayout.OldMetadataFolderName)));
    });

    [Fact]
    public void ATopicOfTheRoot_DroppedOnAnotherTopic_IsATopicThereToo_AndCtrlZPutsItBack() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var personale = Path.Combine(_root, "Personale");

        // As from the search results or from Explorer: its folder, not the topic dragged in the sidebar.
        viewModel.Transfer([personale], Path.Combine(_root, "Lavoro"), TransferKind.Move);

        var lavoro = viewModel.Topics.Single(topic => topic.Name == "Lavoro");
        Assert.Equal(["Personale"], lavoro.Children.Select(child => child.Name));
        Assert.True(MetadataStore.Load(_root).IsTopic("Lavoro/Personale"));
        Assert.DoesNotContain(viewModel.Tiles, tile => tile.IsGeneral);

        viewModel.Undo();

        Assert.True(Directory.Exists(personale));
        Assert.Equal(["Lavoro", "Personale"], viewModel.Topics.Select(topic => topic.Name));
    });

    [Fact]
    public void ARowMovedToAnotherTopic_StaysARow_AlsoWhenCtrlZBringsItBack() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        viewModel.DemoteTile(viewModel.Tiles.Single(tile => tile.Title == "Archivio"));

        viewModel.Transfer([archive], Path.Combine(_root, "Personale"), TransferKind.Move);
        Assert.True(MetadataStore.Load(_root).IsItem("Personale/Archivio"));

        viewModel.Undo();

        Assert.True(Directory.Exists(archive));
        Assert.True(MetadataStore.Load(_root).IsItem("Lavoro/Archivio"));
        Assert.Equal(["General", "Documenti"], viewModel.Tiles.Select(tile => tile.Title));
    });

    [Fact]
    public void ANewTile_ComesWithItsNameReadyToType() => StaThread.Run(() =>
    {
        using var viewModel = Open();

        viewModel.NewTile();
        viewModel.NewTile();

        var tile = Assert.Single(viewModel.Tiles, tile => tile.IsEditing);
        Assert.Equal("New tile (2)", tile.Title);
        Assert.Equal("New tile (2)", tile.EditName);
    });

    [Fact]
    public void TilesKeepThePlaceTheyWereMovedTo() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        Assert.Equal(["Archivio", "Documenti"], viewModel.Tiles.Select(tile => tile.Title));

        viewModel.MoveTile(viewModel.Tiles.Single(tile => tile.Title == "Documenti"), viewModel.Tiles.Single(tile => tile.Title == "Archivio"));

        Assert.Equal(["Documenti", "Archivio"], viewModel.Tiles.Select(tile => tile.Title));
        using var reopened = Open();
        Assert.Equal(["Documenti", "Archivio"], reopened.Tiles.Select(tile => tile.Title));
    });

    [Fact]
    public void SearchResults_AreNotRenamedInPlace() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.BuildIndexNow();
        viewModel.Query = "bollette";
        var result = Assert.Single(viewModel.Results);

        viewModel.BeginRename(result);

        Assert.False(result.IsEditing);
        Assert.Contains("Search results", viewModel.Status);
    });

    [Fact]
    public void TheFiltersOfTheSearch_WriteInTheSearchBox_AndShowWhatIsWrittenThere() => StaThread.Run(() =>
    {
        AddFile("Lavoro/Documenti/offerta.pdf");
        AddFile("Personale/Casa/offerta.pdf");
        using var viewModel = Open();
        viewModel.BuildIndexNow();
        var pdf = viewModel.KindFilters.Single(filter => filter.Kind.Name == "pdf");
        var lavoro = viewModel.Topics.Single(topic => topic.Name == "Lavoro");

        viewModel.Query = "offerta";
        viewModel.ToggleKindFilter(pdf.Kind);

        Assert.Equal("offerta type:pdf", viewModel.Query);
        Assert.True(pdf.IsChecked);
        Assert.Equal(2, viewModel.Results.Count);

        viewModel.SetSearchPlace(lavoro);

        Assert.Equal("offerta type:pdf in:/Lavoro", viewModel.Query);
        Assert.Equal("In: Lavoro", viewModel.PlaceFilterText);
        Assert.True(viewModel.IsSearchPlace(lavoro));
        Assert.Equal(Path.Combine(_root, "Lavoro", "Documenti", "offerta.pdf"), Assert.Single(viewModel.Results).Item.FullPath);

        // Filters alone list what passes them; written by hand, they show the same.
        viewModel.Query = "type:pdf";
        Assert.Equal("2 items, most recent first", viewModel.ResultsHeader);
        Assert.Equal("Everywhere", viewModel.PlaceFilterText);
        Assert.False(viewModel.HasPlaceFilter);
        viewModel.Query = "type:photos";
        Assert.False(pdf.IsChecked);
        Assert.True(viewModel.KindFilters.Single(filter => filter.Kind.Name == "images").IsChecked);
        Assert.Equal("Nothing matches these filters.", viewModel.ResultsHeader);
    });

    // ---- Ctrl+Z ----

    [Fact]
    public void Undo_PutsMovedFilesBack_Selected() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var documents = Path.Combine(_root, "Lavoro", "Documenti");
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        viewModel.Transfer([Path.Combine(documents, "a.txt"), Path.Combine(documents, "b.txt")], archive, TransferKind.Move);
        Assert.Equal("Undo move", viewModel.UndoLabel);

        viewModel.Undo();

        Assert.True(File.Exists(Path.Combine(documents, "a.txt")));
        Assert.True(File.Exists(Path.Combine(documents, "b.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(archive));
        Assert.Equal(["a.txt", "b.txt"], viewModel.SelectedItems().Select(item => item.Name));
        Assert.False(viewModel.CanUndo);
    });

    [Fact]
    public void Undo_BringsAMovedTopicBack_StillATopic_ThenTakesBackItsPromotion() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var folder = Path.Combine(_root, "Lavoro", "Archivio");
        viewModel.PromoteTile(viewModel.Tiles.Single(tile => tile.Title == "Archivio"));
        viewModel.Transfer([folder], Path.Combine(_root, "Personale"), TransferKind.Move);
        Assert.True(MetadataStore.Load(_root).IsTopic("Personale/Archivio"));

        viewModel.Undo();

        Assert.True(Directory.Exists(folder));
        var metadata = MetadataStore.Load(_root);
        Assert.True(metadata.IsTopic("Lavoro/Archivio"));
        Assert.DoesNotContain("Personale/Archivio", metadata.Folders.Keys);

        viewModel.Undo();

        Assert.False(MetadataStore.Load(_root).IsTopic("Lavoro/Archivio"));
        Assert.Contains(viewModel.Tiles, tile => tile.Title == "Archivio");
    });

    [Fact]
    public void Undo_GivesBackTheOldName() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var item = Item(viewModel, "a.txt");
        viewModel.BeginRename(item);
        item.EditName = "c.txt";
        viewModel.CommitRename(item);
        Assert.Equal("Undo rename", viewModel.UndoLabel);

        viewModel.Undo();

        Assert.True(File.Exists(Path.Combine(_root, "Lavoro", "Documenti", "a.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "Lavoro", "Documenti", "c.txt")));
    });

    [Fact]
    public void Undo_TakesBackANewTileAndItsName_OneStepAtATime() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.NewTile();
        var tile = Assert.Single(viewModel.Tiles, tile => tile.IsEditing);
        tile.EditName = "Fornitori";
        viewModel.CommitRename(tile);

        viewModel.Undo();
        Assert.True(Directory.Exists(Path.Combine(_root, "Lavoro", "New tile")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Lavoro", "Fornitori")));

        viewModel.Undo();
        Assert.False(Directory.Exists(Path.Combine(_root, "Lavoro", "New tile")));
    });

    [Fact]
    public void Undo_LeavesANewFolderThatIsNoLongerEmpty() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.NewFolder(viewModel.Tiles.Single(tile => tile.Title == "Documenti"));
        var folder = Path.Combine(_root, "Lavoro", "Documenti", "New folder");
        File.WriteAllText(Path.Combine(folder, "importante.txt"), "");

        viewModel.Undo();

        Assert.True(File.Exists(Path.Combine(folder, "importante.txt")));
        Assert.Contains("no longer empty", viewModel.Status);
    });

    [Fact]
    public void Undo_PutsTheTilesBackInTheirOrder() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.MoveTile(viewModel.Tiles.Single(tile => tile.Title == "Documenti"), viewModel.Tiles.Single(tile => tile.Title == "Archivio"));

        viewModel.Undo();

        Assert.Equal(["Archivio", "Documenti"], viewModel.Tiles.Select(tile => tile.Title));
        Assert.DoesNotContain(MetadataStore.Load(_root).Folders.Values, folder => folder.Order is not null);
    });

    [Fact]
    public void ATopicThatLeftTheArchive_IsATopicAgainWhenItComesBack() => StaThread.Run(() =>
    {
        var outside = Directory.CreateDirectory(_root + "-fuori").FullName;
        try
        {
            using var viewModel = Open();
            viewModel.PromoteTile(viewModel.Tiles.Single(tile => tile.Title == "Archivio"));
            viewModel.Transfer([Path.Combine(_root, "Lavoro", "Archivio")], outside, TransferKind.Move);
            Assert.False(MetadataStore.Load(_root).IsTopic("Lavoro/Archivio"));

            viewModel.Undo();

            Assert.True(Directory.Exists(Path.Combine(_root, "Lavoro", "Archivio")));
            Assert.True(MetadataStore.Load(_root).IsTopic("Lavoro/Archivio"));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    });

    [Fact]
    public void UndoingTheOrderOfTiles_TouchesNothingElse() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.MoveTile(viewModel.Tiles.Single(tile => tile.Title == "Documenti"), viewModel.Tiles.Single(tile => tile.Title == "Archivio"));
        // Changed meanwhile without a step of its own, like a folder merged into a tile.
        var metadata = MetadataStore.Load(_root);
        metadata.SetTopic("Lavoro/Documenti/Contratti", true);
        MetadataStore.Save(_root, metadata);

        viewModel.Undo();

        Assert.Equal(["Archivio", "Documenti"], viewModel.Tiles.Select(tile => tile.Title));
        Assert.True(MetadataStore.Load(_root).IsTopic("Lavoro/Documenti/Contratti"));
    });

    [Fact]
    public void Undo_LeavesAloneWhatChangedSince() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var documents = Path.Combine(_root, "Lavoro", "Documenti");
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        viewModel.Transfer([Path.Combine(documents, "a.txt")], archive, TransferKind.Move);
        // Renamed in Explorer meanwhile.
        File.Move(Path.Combine(archive, "a.txt"), Path.Combine(archive, "z.txt"));

        viewModel.Undo();

        Assert.True(File.Exists(Path.Combine(archive, "z.txt")));
        Assert.False(File.Exists(Path.Combine(documents, "a.txt")));
        Assert.Contains("can't be undone", viewModel.Status);
    });

    [Fact]
    public void Undo_WithNothingToUndo_SaysSo() => StaThread.Run(() =>
    {
        using var viewModel = Open();

        viewModel.Undo();

        Assert.Equal("There is nothing to undo.", viewModel.Status);
    });

    [Fact]
    public void ACopyNextToTheOriginal_IsRememberedUnderItsOwnName_AndSelected() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var original = Path.Combine(_root, "Lavoro", "Documenti", "a.txt");

        viewModel.Transfer([original], Path.GetDirectoryName(original)!, TransferKind.Copy);

        var copy = Assert.Single(Assert.IsType<CopyStep>(viewModel.LastUndoStep).Copies);
        Assert.NotEqual(original, copy, StringComparer.OrdinalIgnoreCase);
        Assert.True(File.Exists(copy));
        Assert.Equal([Path.GetFileName(copy)], viewModel.SelectedItems().Select(item => item.Name));
    });

    [Fact]
    public void AShortcut_LeavesTheOriginalWhereItIs_AndCanBeUndone() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var original = Path.Combine(_root, "Personale", "Casa", "bollette.csv");

        viewModel.Transfer([original], Path.Combine(_root, "Lavoro", "Archivio"), TransferKind.Link);

        Assert.True(File.Exists(original));
        var link = Assert.Single(Assert.IsType<LinkStep>(viewModel.LastUndoStep).Links);
        Assert.Equal(Path.Combine(_root, "Lavoro", "Archivio", "bollette.csv.lnk"), link);
        Assert.Equal(["bollette.csv"], viewModel.SelectedItems().Select(item => item.Name));
        Assert.Equal("Undo shortcut", viewModel.UndoLabel);
    });

    [Fact]
    public void ANewLinkToASite_StartsWithItsNameReadyToType() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var folder = Path.Combine(_root, "Lavoro", "Archivio");

        viewModel.AskNewLink(folder);
        var prompt = Assert.IsType<PromptViewModel>(viewModel.Prompt);
        prompt.Text = "www.rossi.it/ordini";
        prompt.ConfirmCommand.Execute(null);

        Assert.Null(viewModel.Prompt);
        Assert.True(File.Exists(Path.Combine(folder, "rossi.it.url")));
        var item = Assert.Single(viewModel.SelectedItems());
        Assert.True(item.IsEditing);
        Assert.Equal("rossi.it", item.EditName);
        Assert.Equal("Undo shortcut", viewModel.UndoLabel);
    });

    [Fact]
    public void ANewLinkToNothing_SaysWhy_AndWaits() => StaThread.Run(() =>
    {
        using var viewModel = Open();

        viewModel.AskNewLink(Path.Combine(_root, "Lavoro", "Archivio"));
        var prompt = Assert.IsType<PromptViewModel>(viewModel.Prompt);
        prompt.Text = @"C:\Non esiste\niente.exe";
        prompt.ConfirmCommand.Execute(null);

        Assert.Same(prompt, viewModel.Prompt);
        Assert.Contains("can't be found", prompt.Error);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_root, "Lavoro", "Archivio")));
    });

    // ---- The root of the archive ----

    [Fact]
    public void FilesLooseInTheRoot_AreToldInTheStatusBar_AndTheSearchFindsThem() => StaThread.Run(() =>
    {
        using (var viewModel = Open())
            Assert.Null(viewModel.RootFilesText);

        AddFile("scaricato.pdf");
        AddFile("scansione.pdf");
        using var reopened = Open();

        Assert.Equal("2 loose files in the vault", reopened.RootFilesText);
        // They are in no topic, but the search finds them.
        reopened.BuildIndexNow();
        reopened.Query = "scaricato";
        Assert.Equal("Vault", Assert.Single(reopened.Results).Location);
    });

    [Fact]
    public void AToast_StaysUntilThePanelOpensAgain_AndTheStatusBarTellsAboutTheArchive() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.Undo();

        viewModel.BuildIndexNow();
        Assert.Equal("There is nothing to undo.", Assert.Single(viewModel.Toasts).Title);
        // Lavoro/Documenti/a.txt, b.txt and Lavoro/Archivio, with their folders and topics.
        Assert.Matches(@"^\d+ items · \d+(\.\d+)? (bytes|KB)$", viewModel.ArchiveSummary);
        Assert.NotNull(viewModel.DiskText);
        Assert.Contains("free of", viewModel.DiskToolTip);

        viewModel.OnPanelShowing();
        Assert.Empty(viewModel.Toasts);
    });

    [Fact]
    public void TheStatusBar_SaysHowMuchIsSelected() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        File.WriteAllText(Path.Combine(_root, "Lavoro", "Documenti", "a.txt"), new string('a', 2048));
        viewModel.OnPanelShowing();

        Item(viewModel, "a.txt").IsSelected = true;
        Item(viewModel, "b.txt").IsSelected = true;
        viewModel.UpdateSelectionSummary();

        Assert.StartsWith("2 items selected · 2.", viewModel.SelectionSummary);
        Assert.EndsWith(" KB", viewModel.SelectionSummary);

        Item(viewModel, "a.txt").IsSelected = false;
        Item(viewModel, "b.txt").IsSelected = false;
        viewModel.UpdateSelectionSummary();

        Assert.Null(viewModel.SelectionSummary);
    });

    [Fact]
    public void ANewVersionReady_ShowsInTheStatusBar_AndItsButtonAsksToRestart() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var asked = 0;
        viewModel.RestartToUpdateRequested += (_, _) => asked++;
        Assert.Null(viewModel.UpdateText);

        viewModel.UpdateReady = "0.4.0";
        viewModel.RestartToUpdate();

        Assert.Equal("Inlay 0.4.0 is ready", viewModel.UpdateText);
        Assert.Equal(1, asked);
    });

    [Fact]
    public void TheKeysWorthKnowing_SayTheShortcutInUse() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Contains("Alt+E", Keys(viewModel));

        // Changed in the settings: the «?» of the status bar says the new one.
        viewModel.Hotkey = "Ctrl+Alt+Q";

        Assert.Contains(nameof(MainViewModel.Shortcuts), changed);
        Assert.Contains("Ctrl+Alt+Q", Keys(viewModel));
        Assert.DoesNotContain("Alt+E", Keys(viewModel));

        static List<string> Keys(MainViewModel model) => model.Shortcuts.SelectMany(group => group.Hints).SelectMany(hint => hint.Keys).ToList();
    });

    [Fact]
    public void ACopyThatWouldNotFit_IsNotStarted_AndSaysWhy() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var big = Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N") + ".bin");
        Directory.CreateDirectory(Path.GetDirectoryName(big)!);
        File.WriteAllBytes(big, new byte[64 * 1024]);
        try
        {
            // A disk with 10 KB left.
            viewModel.DiskOf = _ => new DiskSpace("Z:", 10 * 1024, 100L * 1024 * 1024 * 1024);
            var destination = Path.Combine(_root, "Lavoro", "Archivio");

            viewModel.Transfer([big], destination, TransferKind.Copy).GetAwaiter().GetResult();

            Assert.False(File.Exists(Path.Combine(destination, Path.GetFileName(big))));
            var toast = Assert.Single(viewModel.Toasts);
            Assert.Equal(ToastKind.Warning, toast.Kind);
            Assert.Equal("Not enough space on Z:", toast.Title);
            Assert.StartsWith("The files need 64.0 KB, and 10.0 KB is left.", toast.Body);
            Assert.False(toast.ClosesByItself);
            Assert.True(viewModel.IsDiskLow);
            Assert.StartsWith("Low on space: ", viewModel.DiskText);
        }
        finally
        {
            File.Delete(big);
        }
    });

    [Fact]
    public void OpeningThePanel_ReadsTheBoardAgain() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        Assert.DoesNotContain(viewModel.Tiles.SelectMany(tile => tile.Items), item => item.Name == "nuovo.txt");
        // A file that arrived while the panel was hidden, on a drive without a watcher to report it.
        AddFile("Lavoro/Documenti/nuovo.txt");

        viewModel.OnPanelShowing();

        Assert.Contains(viewModel.Tiles.SelectMany(tile => tile.Items), item => item.Name == "nuovo.txt");
    });

    [Fact]
    public void AReload_KeepsTheViewModelsOfWhatIsStillThere_WithTheirState() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var topic = viewModel.SelectedTopic!;
        var tile = viewModel.Tiles.Single(candidate => candidate.Title == "Documenti");
        var item = Item(viewModel, "a.txt");
        item.IsSelected = true;
        viewModel.Topics.Single(candidate => candidate.Name == "Personale").IsExpanded = false;
        AddFile("Lavoro/Documenti/nuovo.txt");

        viewModel.OnPanelShowing();

        Assert.Same(topic, viewModel.SelectedTopic);
        Assert.Same(tile, viewModel.Tiles.Single(candidate => candidate.Title == "Documenti"));
        Assert.Same(item, Item(viewModel, "a.txt"));
        Assert.True(item.IsSelected);
        Assert.False(viewModel.Topics.Single(candidate => candidate.Name == "Personale").IsExpanded);
        Assert.Equal(["a.txt", "b.txt", "nuovo.txt"], tile.Items.Select(candidate => candidate.Name));
        Assert.Equal(3, tile.Count);
    });

    [Fact]
    public void ASavedFile_ShowsItsNewDate_OnTheSameRow() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var item = Item(viewModel, "a.txt");
        var dates = 0;
        item.PropertyChanged += (_, e) => dates += e.PropertyName == nameof(ItemViewModel.DateText) ? 1 : 0;
        Assert.Equal("today", item.DateText);
        File.SetLastWriteTime(Path.Combine(_root, "Lavoro", "Documenti", "a.txt"), DateTime.Now.AddDays(-1));

        viewModel.OnPanelShowing();

        Assert.Same(item, Item(viewModel, "a.txt"));
        Assert.Equal("yesterday", item.DateText);
        Assert.Equal(1, dates);
    });

    [Fact]
    public void MovingATile_ReordersTheSameViewModels() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var archivio = viewModel.Tiles.Single(tile => tile.Title == "Archivio");
        var documenti = viewModel.Tiles.Single(tile => tile.Title == "Documenti");

        viewModel.MoveTile(documenti, before: archivio);

        Assert.Equal([documenti, archivio], viewModel.Tiles);
    });

    // ---- Topics in their order, tiles in their size, items in their order ----

    [Fact]
    public void ATopicDraggedAboveAnother_KeepsThatPlace_AndCtrlZPutsItBack() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var lavoro = viewModel.Topics.Single(topic => topic.Name == "Lavoro");
        var personale = viewModel.Topics.Single(topic => topic.Name == "Personale");

        viewModel.MoveTopic(personale, parent: null, before: lavoro);

        Assert.Equal([personale, lavoro], viewModel.Topics);
        using (var reopened = Open())
            Assert.Equal(["Personale", "Lavoro"], reopened.Topics.Select(topic => topic.Name));
        Assert.Equal("Undo space move", viewModel.UndoLabel);

        viewModel.Undo();

        Assert.Equal([lavoro, personale], viewModel.Topics);
    });

    [Fact]
    public void ATopicDroppedInsideAnother_TakesItsFolderThere_StillATopic_AndCtrlZBringsItBack() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var lavoro = viewModel.Topics.Single(topic => topic.Name == "Lavoro");
        var personale = viewModel.Topics.Single(topic => topic.Name == "Personale");
        viewModel.MoveTopic(personale, parent: null, before: lavoro);
        viewModel.SelectTopicCommand.Execute(personale);

        viewModel.MoveTopic(personale, parent: lavoro, before: null);

        Assert.True(File.Exists(Path.Combine(_root, "Lavoro", "Personale", "Casa", "bollette.csv")));
        Assert.True(MetadataStore.Load(_root).IsTopic("Lavoro/Personale"));
        var only = Assert.Single(viewModel.Topics);
        var moved = Assert.Single(only.Children);
        Assert.Equal("Personale", moved.Name);
        // The board stays on it, and its folder is still a tile there.
        Assert.Same(moved, viewModel.SelectedTopic);
        Assert.Equal(["Casa"], viewModel.Tiles.Select(tile => tile.Title));

        viewModel.Undo();

        Assert.True(File.Exists(Path.Combine(_root, "Personale", "Casa", "bollette.csv")));
        Assert.Equal(["Personale", "Lavoro"], viewModel.Topics.Select(topic => topic.Name));
    });

    [Fact]
    public void ATopic_DoesNotGoInsideOneOfItsOwn() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var lavoro = viewModel.Topics.Single(topic => topic.Name == "Lavoro");
        viewModel.NewTopic(lavoro);
        var inside = Assert.Single(lavoro.Children);

        Assert.False(MainViewModel.CanGoUnder(lavoro, inside));
        viewModel.MoveTopic(lavoro, parent: inside, before: null);

        Assert.True(Directory.Exists(Path.Combine(_root, "Lavoro", "New space")));
        Assert.Same(lavoro, viewModel.Topics.Single(topic => topic.Name == "Lavoro"));
    });

    [Fact]
    public void AResizedTile_KeepsItsSize_AndCtrlZGivesBackTheUsualOne() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var tile = viewModel.Tiles.Single(candidate => candidate.Title == "Documenti");
        Assert.Equal(0, tile.Rows);
        Assert.Equal(TileViewModel.UsualListHeight, tile.ListMaxHeight);

        viewModel.ResizeTile(tile, new TileSize(2, 15));

        Assert.Equal(new TileSize(2, 15), tile.Size);
        Assert.Equal(2, tile.Columns);
        Assert.Equal(15, tile.Rows);
        Assert.Equal(double.PositiveInfinity, tile.ListMaxHeight);
        Assert.Equal(new TileSize(2, 15), MetadataStore.Load(_root).SizeOf("Lavoro/Documenti"));
        Assert.Equal("Undo resize", viewModel.UndoLabel);

        viewModel.Undo();

        Assert.Null(tile.Size);
        Assert.Equal(1, tile.Columns);
        Assert.DoesNotContain("Lavoro/Documenti", MetadataStore.Load(_root).Folders.Keys);
    });

    [Fact]
    public void ATileSortedByDate_ShowsTheNewestFirst_EitherWay_AndCtrlZTakesItBack() => StaThread.Run(() =>
    {
        File.SetLastWriteTime(Path.Combine(_root, "Lavoro", "Documenti", "a.txt"), DateTime.Now.AddDays(-2));
        using var viewModel = Open();
        var tile = viewModel.Tiles.Single(candidate => candidate.Title == "Documenti");
        Assert.Equal(SortKey.Name, tile.SortedBy);

        viewModel.SortTile(tile, SortKey.Modified);
        Assert.Equal(["b.txt", "a.txt"], tile.Items.Select(item => item.Name));
        Assert.Equal(SortKey.Modified, tile.SortedBy);
        Assert.True(tile.IsDescending);

        viewModel.SetSortDirection(tile, descending: false);
        Assert.Equal(["a.txt", "b.txt"], tile.Items.Select(item => item.Name));
        Assert.Equal("Undo sort", viewModel.UndoLabel);

        viewModel.Undo();
        Assert.Equal(["b.txt", "a.txt"], tile.Items.Select(item => item.Name));
        viewModel.Undo();
        Assert.Equal(["a.txt", "b.txt"], tile.Items.Select(item => item.Name));
        Assert.Equal(SortKey.Name, tile.SortedBy);
    });

    [Fact]
    public void ItemsDraggedInTheirTile_KeepThatOrder_NewOnesFollow_AndARenameKeepsItsPlace() => StaThread.Run(() =>
    {
        AddFile("Lavoro/Documenti/c.txt");
        using var viewModel = Open();
        var tile = viewModel.Tiles.Single(candidate => candidate.Title == "Documenti");

        viewModel.MoveItems(tile, [Item(viewModel, "c.txt")], before: Item(viewModel, "a.txt"));

        Assert.Equal(["c.txt", "a.txt", "b.txt"], tile.Items.Select(item => item.Name));
        Assert.True(tile.ByHand);
        Assert.Null(tile.SortedBy);
        Assert.Equal(["c.txt"], viewModel.SelectedItems().Select(item => item.Name));

        AddFile("Lavoro/Documenti/0.txt");
        var renamed = Item(viewModel, "a.txt");
        viewModel.BeginRename(renamed);
        renamed.EditName = "z.txt";
        Assert.Null(viewModel.CommitRename(renamed));
        Assert.Equal(["c.txt", "z.txt", "b.txt", "0.txt"], tile.Items.Select(item => item.Name));

        // «Sort by» takes the place of the order given by hand.
        viewModel.SortTile(tile, SortKey.Name);
        Assert.Equal(["0.txt", "b.txt", "c.txt", "z.txt"], tile.Items.Select(item => item.Name));
        Assert.False(tile.ByHand);
    });

    // ---- The canvas ----

    [Fact]
    public void TheCanvas_HasAZoneForEachTopic_AndTopicsInsideTheirTopic() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.PromoteTile(viewModel.Tiles.Single(tile => tile.Title == "Archivio"));

        viewModel.IsCanvasShown = true;

        Assert.False(viewModel.IsBoardVisible);
        Assert.True(viewModel.IsCanvasVisible);
        Assert.Equal(["Lavoro", "Personale"], viewModel.Canvas.Zones.Select(zone => zone.Key));
        var lavoro = viewModel.Canvas.Zones[0];
        Assert.Same(viewModel.Topics.Single(topic => topic.Name == "Lavoro"), lavoro.Topic);
        Assert.Equal(["Documenti"], lavoro.Tiles.Select(tile => tile.Title));
        Assert.Equal(["Lavoro/Archivio"], lavoro.Children.Select(zone => zone.Key));
        // Archivio, now a topic, is a zone inside and no longer a tile.
        Assert.Equal("1 tile · 2 items", lavoro.Summary);
        Assert.Equal(["Casa"], viewModel.Canvas.Zones[1].Tiles.Select(tile => tile.Title));
    });

    [Fact]
    public void OnTheCanvas_TheSelectionTakesItemsOfAnyZone_AndARenameKeepsIt() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.IsCanvasShown = true;
        var a = CanvasItem(viewModel, "a.txt");
        var bollette = CanvasItem(viewModel, "bollette.csv");
        a.IsSelected = true;
        bollette.IsSelected = true;
        Assert.Equal(["a.txt", "bollette.csv"], viewModel.SelectedItems().Select(item => item.Name));

        bollette.IsSelected = false;
        viewModel.BeginRename(a);
        a.EditName = "c.txt";
        Assert.Null(viewModel.CommitRename(a));

        Assert.True(File.Exists(Path.Combine(_root, "Lavoro", "Documenti", "c.txt")));
        Assert.Equal(["c.txt"], viewModel.SelectedItems().Select(item => item.Name));
    });

    [Fact]
    public void AMoveBetweenZones_SelectsTheItemsWhereTheyLanded_AndTheZonesKeepTheirViewModels() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.IsCanvasShown = true;
        var personale = viewModel.Canvas.Zones.Single(zone => zone.Key == "Personale");
        var casa = personale.Tiles.Single();

        viewModel.Transfer([Path.Combine(_root, "Lavoro", "Documenti", "a.txt")], casa.FolderPath, TransferKind.Move);

        Assert.Same(personale, viewModel.Canvas.Zones.Single(zone => zone.Key == "Personale"));
        Assert.Same(casa, personale.Tiles.Single());
        Assert.Equal(["a.txt", "bollette.csv"], casa.Items.Select(item => item.Name));
        Assert.Equal(["a.txt"], viewModel.SelectedItems().Select(item => item.Name));
        Assert.Equal("1 tile · 2 items", personale.Summary);
    });

    [Fact]
    public void ATileMovedOnTheCanvas_ChangesPlaceInItsZone() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.IsCanvasShown = true;
        var lavoro = viewModel.Canvas.Zones.Single(zone => zone.Key == "Lavoro");
        Assert.Equal(["Archivio", "Documenti"], lavoro.Tiles.Select(tile => tile.Title));

        viewModel.MoveTile(lavoro.Tiles[1], before: lavoro.Tiles[0]);

        Assert.Equal(["Documenti", "Archivio"], lavoro.Tiles.Select(tile => tile.Title));
        viewModel.IsCanvasShown = false;
        Assert.Equal(["Documenti", "Archivio"], viewModel.Tiles.Select(tile => tile.Title));
    });

    private static ItemViewModel CanvasItem(MainViewModel viewModel, string name) =>
        viewModel.Canvas.AllTiles().SelectMany(tile => tile.Items).Single(item => item.Name == name);

    // ---- «Move to…» ----

    [Fact]
    public void MoveTo_OffersTilesAndTopics_ExceptWhereTheItemsAre_RecentFirst() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        _recent.Remember(_root, "Personale/Casa");
        var item = Item(viewModel, "a.txt");

        viewModel.AskDestination([item.Item.FullPath]);

        var picker = Assert.IsType<DestinationPickerViewModel>(viewModel.Picker);
        Assert.Equal("Move “a.txt” to…", picker.Title);
        Assert.Equal("Move", picker.ConfirmLabel);
        // Not Documenti, where a.txt already is; Casa first, as the place used last.
        Assert.Equal(["Casa", "Lavoro", "Archivio", "Personale"], picker.Candidates.Select(candidate => candidate.Title));
        Assert.True(picker.Candidates[0].IsRecent);
        Assert.Same(picker.Candidates[0], picker.Selected);

        picker.Filter = "arch";
        Assert.Equal(["Archivio"], picker.Candidates.Select(candidate => candidate.Title));
    });

    [Fact]
    public void MoveTo_MovesTheFiles_RemembersThePlace_AndOffersUndoAndShow() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var source = Path.Combine(_root, "Lavoro", "Documenti", "a.txt");
        viewModel.AskDestination([source]);
        var picker = viewModel.Picker!;
        picker.Filter = "casa";

        picker.ConfirmCommand.Execute(null);

        Assert.Null(viewModel.Picker);
        var moved = Path.Combine(_root, "Personale", "Casa", "a.txt");
        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(source));
        Assert.Equal(["Personale/Casa"], _recent.For(_root));
        Assert.Equal("“a.txt” moved to “Casa”.", viewModel.Status);
        var toast = Assert.Single(viewModel.Toasts);
        Assert.Equal(ToastKind.Success, toast.Kind);
        Assert.True(toast.CanUndo);
        Assert.True(toast.CanShow);
        Assert.True(toast.ClosesByItself);
        Assert.IsType<MoveStep>(viewModel.LastUndoStep);

        viewModel.ShowFrom(toast);
        Assert.Equal("Personale", viewModel.BoardTitle);
        Assert.Equal(["a.txt"], viewModel.SelectedItems().Select(selected => selected.Name));

        viewModel.UndoFrom(toast);
        Assert.True(File.Exists(source));
        // Undone: its toast goes, and the one of the undo takes its place, with nothing more to take back.
        var undone = Assert.Single(viewModel.Toasts);
        Assert.StartsWith("Move undone", undone.Title);
        Assert.False(undone.HasActions);
    });

    [Fact]
    public void MoveTo_LinksWhatHasAPlaceOfItsOwn() => StaThread.Run(() =>
    {
        var program = Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(program, "");
        try
        {
            using var viewModel = Open();
            viewModel.AskDestination([program]);
            var picker = viewModel.Picker!;
            Assert.StartsWith("Create a shortcut to", picker.Title);
            Assert.Equal("Create shortcut", picker.ConfirmLabel);
            picker.Filter = "archivio";

            picker.ConfirmCommand.Execute(null);

            Assert.True(File.Exists(program));
            var link = Assert.Single(Assert.IsType<LinkStep>(viewModel.LastUndoStep).Links);
            Assert.Equal(Path.Combine(_root, "Lavoro", "Archivio"), Path.GetDirectoryName(link));
            Assert.StartsWith("Shortcut “", viewModel.Status);
            Assert.True(Assert.Single(viewModel.Toasts).CanShow);
        }
        finally
        {
            File.Delete(program);
        }
    });

    [Fact]
    public void AColor_StaysOnTheTile_InTheMetadata_AndCtrlZTakesItBack() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var tile = viewModel.Tiles.Single(candidate => candidate.Title == "Documenti");

        viewModel.SetTileColor(tile, "#FFA133");

        Assert.Same(tile, viewModel.Tiles.Single(candidate => candidate.Title == "Documenti"));
        Assert.Equal("#FFA133", tile.Color);
        Assert.True(tile.HasColor);
        Assert.Equal("#FFA133", MetadataStore.Load(_root).ColorOf("Lavoro/Documenti"));
        Assert.Equal("Undo color", viewModel.UndoLabel);

        viewModel.Undo();

        Assert.Null(tile.Color);
        Assert.False(tile.HasColor);
        Assert.DoesNotContain("Lavoro/Documenti", MetadataStore.Load(_root).Folders.Keys);
        Assert.Contains("no color", viewModel.Status);
    });

    [Fact]
    public void FromAfar_ATileTellsItsFirstItems_OneALine() => StaThread.Run(() =>
    {
        for (var i = 1; i <= 14; i++)
            AddFile($"Personale/Casa/conto {i:00}.pdf");
        using var viewModel = Open();
        var documents = viewModel.Tiles.Single(tile => tile.Title == "Documenti");

        Assert.Equal(["a.txt", "b.txt"], documents.Preview.Split(Environment.NewLine));

        viewModel.SelectTopicByPath("Personale");
        var casa = viewModel.Tiles.Single(tile => tile.Title == "Casa");
        // No more than a card from afar has room for, in the order of the tile.
        var names = casa.Preview.Split(Environment.NewLine);
        Assert.Equal(12, names.Length);
        Assert.Equal("bollette.csv", names[0]);
        Assert.Equal("conto 11.pdf", names[^1]);
        Assert.Equal(15, casa.Count);
    });

    [Fact]
    public void TheTopics_GetTheirColorsWhenFirstSeen_KeptInTheArchive() => StaThread.Run(() =>
    {
        using var viewModel = Open();

        var metadata = MetadataStore.Load(_root);
        Assert.Equal([TopicColors.Automatic[0], TopicColors.Automatic[1]], viewModel.Topics.Select(topic => topic.Color));
        Assert.All(viewModel.Topics, topic => Assert.True(topic.HasColor));
        Assert.Equal(TopicColors.Automatic[0], metadata.ColorOf("Lavoro"));
        Assert.True(metadata.IsColorAutomatic("Personale"));
        Assert.All(viewModel.Topics, topic => Assert.Equal(TopicViewModel.AutomaticChoice, topic.ColorChoice));
        // Opened again, the same colors.
        using var again = Open();
        Assert.Equal(viewModel.Topics.Select(topic => topic.Color), again.Topics.Select(topic => topic.Color));
    });

    [Fact]
    public void APreview_ShowsTheColors_AndWritesNothingIntoTheArchive() => StaThread.Run(() =>
    {
        using var viewModel = new MainViewModel(_root, live: false, recent: _recent, readOnly: true);

        Assert.All(viewModel.Topics, topic => Assert.True(topic.HasColor));
        Assert.False(File.Exists(MetadataStore.GetPath(_root)));
    });

    [Fact]
    public void AColorChosenForATopic_Stays_AndCtrlZGivesBackTheOneInlayHadGiven() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var lavoro = viewModel.Topics.Single(topic => topic.Name == "Lavoro");

        viewModel.SetTopicColor(lavoro, "#FF7EB6");

        Assert.Equal("#FF7EB6", lavoro.Color);
        Assert.Equal("#FF7EB6", lavoro.ColorChoice);
        Assert.False(MetadataStore.Load(_root).IsColorAutomatic("Lavoro"));
        Assert.Equal("Undo color", viewModel.UndoLabel);

        viewModel.Undo();

        Assert.Equal(TopicColors.Automatic[0], lavoro.Color);
        Assert.True(MetadataStore.Load(_root).IsColorAutomatic("Lavoro"));

        viewModel.SetTopicColor(lavoro, TopicColors.None);
        Assert.False(lavoro.HasColor);
        Assert.Equal(TopicColors.None, lavoro.ColorChoice);
        // Seen again, a topic left without a color on purpose is not given one.
        using var again = Open();
        Assert.False(again.Topics.Single(topic => topic.Name == "Lavoro").HasColor);
    });

    [Fact]
    public void ATopicInsideAnother_ShowsTheColorOfItsTopic_UnlessOneIsChosenForIt() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.PromoteTile(viewModel.Tiles.Single(tile => tile.Title == "Archivio"));
        var lavoro = viewModel.Topics.Single(topic => topic.Name == "Lavoro");
        var archivio = Assert.Single(lavoro.Children);

        Assert.Equal(lavoro.Color, archivio.Color);
        Assert.Equal(TopicViewModel.AutomaticChoice, archivio.ColorChoice);

        viewModel.SetTopicColor(archivio, "#36CFB0");
        Assert.Equal("#36CFB0", archivio.Color);

        // Auto, inside another topic: that of its topic again, which follows it when it changes.
        viewModel.SetTopicColor(archivio, null);
        viewModel.SetTopicColor(lavoro, "#B48CFF");
        Assert.Equal("#B48CFF", archivio.Color);
        Assert.Null(MetadataStore.Load(_root).ColorOf("Lavoro/Archivio"));
    });

    [Fact]
    public void AnotherOperation_TakesUndoAwayFromTheToastBefore() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var documents = Path.Combine(_root, "Lavoro", "Documenti");
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        viewModel.Transfer([Path.Combine(documents, "a.txt")], archive, TransferKind.Move);
        var first = Assert.Single(viewModel.Toasts);
        Assert.True(first.CanUndo);

        viewModel.Transfer([Path.Combine(documents, "b.txt")], archive, TransferKind.Move);

        // Ctrl+Z would undo the second one now: the first toast still shows where its file went, but not «Undo».
        Assert.Equal(2, viewModel.Toasts.Count);
        Assert.False(first.CanUndo);
        Assert.True(first.CanShow);
        Assert.True(viewModel.Toasts[1].CanUndo);

        viewModel.UndoFrom(first);
        Assert.True(File.Exists(Path.Combine(archive, "b.txt")));
    });

    [Fact]
    public void Problems_StayUntilClosed_TheSameNewsIsNotRepeated_AndThreeAtMost() => StaThread.Run(() =>
    {
        using var viewModel = Open();

        viewModel.Tell(ToastKind.Danger, "Couldn't open “a.txt”.");
        viewModel.Tell(ToastKind.Danger, "Couldn't open “a.txt”.");
        Assert.False(Assert.Single(viewModel.Toasts).ClosesByItself);

        viewModel.Tell(ToastKind.Info, "One.");
        viewModel.Tell(ToastKind.Info, "Two.");
        viewModel.Tell(ToastKind.Info, "Three.");

        Assert.Equal(["One.", "Two.", "Three."], viewModel.Toasts.Select(toast => toast.Title));
        viewModel.DismissToast(viewModel.Toasts[0]);
        Assert.Equal(["Two.", "Three."], viewModel.Toasts.Select(toast => toast.Title));
    });

    private MainViewModel Open() => new(_root, live: false, recent: _recent);

    private static ItemViewModel Item(MainViewModel viewModel, string name) =>
        viewModel.Tiles.SelectMany(tile => tile.Items).Single(item => item.Name == name);

    private void AddFile(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }
}
