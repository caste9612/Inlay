using Quiver.App.ViewModels;
using Quiver.Core;

namespace Quiver.App.Tests;

/// <summary>
/// What the panel does with names and selections, on a real archive in a temporary folder. The view relies on it:
/// a click elsewhere confirms a name only after the click is done, and the selection must survive the reload.
/// </summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "quiver-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public MainViewModelTests()
    {
        AddFile("Lavoro/Documenti/a.txt");
        AddFile("Lavoro/Documenti/b.txt");
        Directory.CreateDirectory(Path.Combine(_root, "Lavoro", "Archivio"));
        AddFile("Personale/Casa/bollette.csv");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

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
        Assert.Equal("Nuova cartella", folder.Name);
        Assert.True(folder.IsEditing);
        Assert.True(Directory.Exists(Path.Combine(_root, "Lavoro", "Documenti", "Nuova cartella")));
    });

    [Fact]
    public void ANewFolderInGenerale_IsANewTile() => StaThread.Run(() =>
    {
        AddFile("Lavoro/Promemoria.txt");
        using var viewModel = Open();

        viewModel.NewFolder(viewModel.Tiles.Single(tile => tile.IsGeneral));

        var tile = Assert.Single(viewModel.Tiles, tile => tile.IsEditing);
        Assert.Equal("Nuovo riquadro", tile.Title);
    });

    [Fact]
    public void ANewTile_ComesWithItsNameReadyToType() => StaThread.Run(() =>
    {
        using var viewModel = Open();

        viewModel.NewTile();
        viewModel.NewTile();

        var tile = Assert.Single(viewModel.Tiles, tile => tile.IsEditing);
        Assert.Equal("Nuovo riquadro (2)", tile.Title);
        Assert.Equal("Nuovo riquadro (2)", tile.EditName);
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
        Assert.Contains("ricerca", viewModel.Status);
    });

    // ---- Ctrl+Z ----

    [Fact]
    public void Undo_PutsMovedFilesBack_Selected() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        var documents = Path.Combine(_root, "Lavoro", "Documenti");
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        viewModel.Transfer([Path.Combine(documents, "a.txt"), Path.Combine(documents, "b.txt")], archive, TransferKind.Move);
        Assert.Equal("Annulla lo spostamento", viewModel.UndoLabel);

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
        Assert.Equal("Annulla la rinomina", viewModel.UndoLabel);

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
        Assert.True(Directory.Exists(Path.Combine(_root, "Lavoro", "Nuovo riquadro")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Lavoro", "Fornitori")));

        viewModel.Undo();
        Assert.False(Directory.Exists(Path.Combine(_root, "Lavoro", "Nuovo riquadro")));
    });

    [Fact]
    public void Undo_LeavesANewFolderThatIsNoLongerEmpty() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.NewFolder(viewModel.Tiles.Single(tile => tile.Title == "Documenti"));
        var folder = Path.Combine(_root, "Lavoro", "Documenti", "Nuova cartella");
        File.WriteAllText(Path.Combine(folder, "importante.txt"), "");

        viewModel.Undo();

        Assert.True(File.Exists(Path.Combine(folder, "importante.txt")));
        Assert.Contains("non è più vuota", viewModel.Status);
    });

    [Fact]
    public void Undo_PutsTheTilesBackInTheirOrder() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.MoveTile(viewModel.Tiles.Single(tile => tile.Title == "Documenti"), viewModel.Tiles.Single(tile => tile.Title == "Archivio"));

        viewModel.Undo();

        Assert.Equal(["Archivio", "Documenti"], viewModel.Tiles.Select(tile => tile.Title));
        Assert.Empty(MetadataStore.Load(_root).Folders);
    });

    [Fact]
    public void ATopicThatLeftTheArchive_IsATopicAgainWhenItComesBack() => StaThread.Run(() =>
    {
        var desktop = Directory.CreateDirectory(_root + "-desktop").FullName;
        try
        {
            using var viewModel = new MainViewModel(_root, live: false, inboxSources: [new InboxSource("Desktop", [desktop])]);
            viewModel.PromoteTile(viewModel.Tiles.Single(tile => tile.Title == "Archivio"));
            viewModel.Transfer([Path.Combine(_root, "Lavoro", "Archivio")], desktop, TransferKind.Move);
            Assert.False(MetadataStore.Load(_root).IsTopic("Lavoro/Archivio"));

            viewModel.Undo();

            Assert.True(Directory.Exists(Path.Combine(_root, "Lavoro", "Archivio")));
            Assert.True(MetadataStore.Load(_root).IsTopic("Lavoro/Archivio"));
        }
        finally
        {
            Directory.Delete(desktop, recursive: true);
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
        Assert.Contains("non si può annullare", viewModel.Status);
    });

    [Fact]
    public void Undo_WithNothingToUndo_SaysSo() => StaThread.Run(() =>
    {
        using var viewModel = Open();

        viewModel.Undo();

        Assert.Equal("Non c'è niente da annullare.", viewModel.Status);
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
        Assert.Equal("Annulla il collegamento", viewModel.UndoLabel);
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
        Assert.Equal("Annulla il collegamento", viewModel.UndoLabel);
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
        Assert.Contains("Non trovo", prompt.Error);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_root, "Lavoro", "Archivio")));
    });

    // ---- Da smistare ----

    [Fact]
    public void TheInbox_ShowsTheDesktop_NextToTheArchivesOwnFiles() => StaThread.Run(() =>
    {
        AddFile("scaricato.pdf");
        var desktop = Directory.CreateDirectory(_root + "-desktop").FullName;
        try
        {
            File.WriteAllText(Path.Combine(desktop, "foto.jpg"), "");
            using var viewModel = new MainViewModel(_root, live: false, inboxSources: [new InboxSource("Desktop", [desktop])]);

            viewModel.ShowInboxCommand.Execute(null);

            Assert.Equal([ArchiveReader.ArchiveInboxTitle, "Desktop"], viewModel.Tiles.Select(tile => tile.Title));
            Assert.Equal(2, viewModel.InboxCount);
            Assert.True(viewModel.IsInInbox(Path.Combine(desktop, "foto.jpg")));
            Assert.False(viewModel.IsInInbox(Path.Combine(_root, "Lavoro", "Documenti", "a.txt")));
        }
        finally
        {
            Directory.Delete(desktop, recursive: true);
        }
    });

    [Fact]
    public void AnOperationsMessage_StaysUntilThePanelOpensAgain() => StaThread.Run(() =>
    {
        using var viewModel = Open();
        viewModel.Undo();

        viewModel.BuildIndexNow();
        Assert.Equal("Non c'è niente da annullare.", viewModel.Status);

        viewModel.OnPanelShowing();
        Assert.StartsWith(_root, viewModel.Status);
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

    private MainViewModel Open() => new(_root, live: false);

    private static ItemViewModel Item(MainViewModel viewModel, string name) =>
        viewModel.Tiles.SelectMany(tile => tile.Items).Single(item => item.Name == name);

    private void AddFile(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }
}
