using System.Runtime.InteropServices;
using EmilsClaudeLaunchpad.Config;
using EmilsClaudeLaunchpad.Discovery;
using static EmilsClaudeLaunchpad.Ui.Theme;

namespace EmilsClaudeLaunchpad;

public sealed class EditorForm : Form
{
    private readonly List<TabPreset> _tabs;
    private readonly List<GroupPreset> _groups;
    private readonly AppSettings _settings;

    private IReadOnlyList<ChatRecord> _chats = Array.Empty<ChatRecord>();
    private string _chatFilter = string.Empty;
    private TextBox _chatSearchBox = null!;
    private FlowLayoutPanel _chatList = null!;
    private FlowLayoutPanel _groupList = null!;
    private Panel _detailScroll = null!;
    private Label _statusLabel = null!;
    private Button _addBtn = null!;

    // Decoupled selection: chat selection (left list) is independent from item selection
    // (right list — group or tab in a group). Both can be active at once.
    private ChatRecord? _selectedChat;
    private object? _selectedItem; // TabPreset or GroupPreset

    // Detail panel — two distinct cards: one for tabs (full form), one for groups (Title + Color only).
    // Visibility is toggled based on what's selected so the user is never editing the wrong shape of data.
    private Panel _tabCard = null!;
    private Panel _groupCard = null!;
    private Label _detailHeader = null!;

    // Tab-card fields
    private TextBox _tabTitleBox = null!;
    private TextBox _tabColorBox = null!;
    private TextBox _cwdBox = null!;
    private ComboBox _promptBox = null!;
    private ComboBox _extraArgsBox = null!;
    private ComboBox _shellBox = null!;
    private Button _tabApplyBtn = null!;

    // Popular slash commands offered in the Prompt dropdown. Empty string = no startup prompt.
    // User can also type freely (DropDownStyle.DropDown allows both pick + type).
    private static readonly string[] PromptOptions =
    {
        "",
        "/remote-control",
        "/compact",
        "/clear",
        "/cost",
        "/status",
        "/help",
        "/memorize",
        "/model",
    };

    private static readonly string[] ShellOptions =
    {
        "",            // blank = use default from AppSettings
        "powershell",  // Windows PowerShell 5.1
        "pwsh",        // PowerShell 7+ (requires install)
        "cmd",
        "wsl",
    };

    private static readonly string[] ExtraArgsOptions =
    {
        "",
        "--debug",
        "--verbose",
        "--print",
        "--dangerously-skip-permissions",
        "--no-cleanup-period-days",
    };

    // Group-card fields
    private TextBox _groupTitleBox = null!;
    private TextBox _groupColorBox = null!;
    private Button _groupApplyBtn = null!;

    public bool SavedChanges { get; private set; }

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private const int WM_NCHITTEST = 0x84;
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;
    private const int HTBOTTOMRIGHT = 17;
    private const int ResizeGripSize = 16;

    public EditorForm(PresetsConfig config)
    {
        _tabs = config.Tabs.ToList();
        _groups = config.Groups.ToList();
        _settings = config.Settings;

        Text = "Edit sessions and groups";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(820, 600);
        BackColor = Bg;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        BuildLayout();
        RefreshChats();
        RefreshGroups();
        ClearDetail();
    }

    protected override void WndProc(ref Message m)
    {
        // Native bottom-right resize when mouse is in the grip area.
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            var lParam = m.LParam.ToInt64();
            short x = (short)(lParam & 0xFFFF);
            short y = (short)((lParam >> 16) & 0xFFFF);
            var pt = PointToClient(new Point(x, y));
            if (pt.X >= ClientSize.Width - ResizeGripSize && pt.Y >= ClientSize.Height - ResizeGripSize)
                m.Result = (IntPtr)HTBOTTOMRIGHT;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Bg);
        using var pen = new Pen(Border, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        // Resize grip — a few dots in the bottom-right corner.
        using var dotBrush = new SolidBrush(TextDim);
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col <= row; col++)
            {
                int x = Width - 5 - col * 4;
                int y = Height - 5 - row * 4;
                e.Graphics.FillRectangle(dotBrush, x, y, 2, 2);
            }
        }
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Bg,
            Padding = new Padding(14, 12, 14, 14),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // === Header (draggable, with title + close X) ===
        var header = BuildHeader();
        root.Controls.Add(header, 0, 0);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var headerDivider = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Border, Margin = new Padding(0, 0, 0, 12) };
        root.Controls.Add(headerDivider, 0, 1);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 13));

        // === Main two-column area (chats | groups) — fills available space ===
        var twoCol = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Bg,
        };
        twoCol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        twoCol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        twoCol.Controls.Add(BuildChatsPane(), 0, 0);
        twoCol.Controls.Add(BuildGroupsPane(), 1, 0);
        root.Controls.Add(twoCol, 0, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // === Detail panel (scrollable, fixed height) ===
        _detailScroll = BuildDetailPanel();
        _detailScroll.Dock = DockStyle.Top;
        _detailScroll.Height = 250;
        _detailScroll.Margin = new Padding(0, 12, 0, 0);
        root.Controls.Add(_detailScroll, 0, 3);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 262));

        // === Footer: status + Cancel/Save ===
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            Height = 42,
            BackColor = Bg,
            Margin = new Padding(0, 12, 0, 0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        _statusLabel = new Label
        {
            Text = string.Empty,
            ForeColor = StatusInfo,
            BackColor = Bg,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F),
            AutoEllipsis = true,
        };
        footer.Controls.Add(_statusLabel, 0, 0);

        var cancelBtn = MakeSecondaryButton("Cancel", bordered: true);
        cancelBtn.Dock = DockStyle.Fill;
        cancelBtn.Margin = new Padding(0, 0, 8, 0);
        cancelBtn.Click += (_, _) => Close();
        footer.Controls.Add(cancelBtn, 1, 0);

        var saveBtn = MakePrimaryButton("Save and close");
        saveBtn.Dock = DockStyle.Fill;
        saveBtn.Margin = new Padding(0, 0, 18, 0); // keep resize-grip area clear
        saveBtn.Click += (_, _) => SaveAndClose();
        footer.Controls.Add(saveBtn, 2, 0);

        root.Controls.Add(footer, 0, 4);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        Controls.Add(root);
    }

    private Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Bg, Cursor = Cursors.SizeAll };

        var titleLabel = new Label
        {
            Text = "Edit sessions and groups",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 0, 0),
            Cursor = Cursors.SizeAll,
        };
        var closeBtn = MakeCloseButton();
        closeBtn.Width = 36;
        closeBtn.Dock = DockStyle.Right;
        closeBtn.Click += (_, _) => Close();
        header.Controls.Add(titleLabel);
        header.Controls.Add(closeBtn);

        // Drag: clicking the header or title moves the form.
        header.MouseDown += Header_MouseDown;
        titleLabel.MouseDown += Header_MouseDown;
        return header;
    }

    private void Header_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    private Panel BuildChatsPane()
    {
        var pane = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(0, 0, 8, 0) };

        var headerRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 32,
            BackColor = Bg,
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        var lbl = new Label
        {
            Text = "Available Claude chats",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var refreshBtn = MakeSecondaryButton("Refresh", bordered: true);
        refreshBtn.Dock = DockStyle.Fill;
        refreshBtn.Click += (_, _) => { RefreshChats(); SetStatus("Chats refreshed.", false); };
        headerRow.Controls.Add(lbl, 0, 0);
        headerRow.Controls.Add(refreshBtn, 1, 0);

        _chatList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Bg,
            Margin = new Padding(0, 6, 0, 6),
        };

        var addRow = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = Bg };
        _addBtn = MakePrimaryButton("Add selected chat to selected group  →");
        _addBtn.Dock = DockStyle.Fill;
        _addBtn.Click += (_, _) => OnAddChatToGroup();
        addRow.Controls.Add(_addBtn);

        var searchRow = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Bg, Padding = new Padding(0, 4, 0, 4) };
        _chatSearchBox = MakeTextBox();
        _chatSearchBox.Dock = DockStyle.Fill;
        _chatSearchBox.PlaceholderText = "Search chats by folder, preview, or session id…";
        _chatSearchBox.TextChanged += (_, _) =>
        {
            _chatFilter = _chatSearchBox.Text.Trim();
            PopulateChatList();
        };
        searchRow.Controls.Add(_chatSearchBox);

        // Add order matters for stacked Top docks: layout iterates children high-z → low-z.
        // headerRow added LAST = highest z = docks first = topmost slot. searchRow added before
        // headerRow lands just below it. Don't reshuffle without re-checking.
        pane.Controls.Add(_chatList);
        pane.Controls.Add(addRow);
        pane.Controls.Add(searchRow);
        pane.Controls.Add(headerRow);
        headerRow.Dock = DockStyle.Top;
        return pane;
    }

    private Panel BuildGroupsPane()
    {
        var pane = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(8, 0, 0, 0) };

        var headerRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            Height = 32,
            BackColor = Bg,
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        var lbl = new Label
        {
            Text = "Your groups",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var newGroupBtn = MakeSecondaryButton("+ Group", bordered: true);
        newGroupBtn.Dock = DockStyle.Fill;
        newGroupBtn.Margin = new Padding(0, 0, 6, 0);
        newGroupBtn.Click += (_, _) => OnNewGroup();
        var deleteBtn = MakeSecondaryButton("- Delete", bordered: true);
        deleteBtn.Dock = DockStyle.Fill;
        deleteBtn.Click += (_, _) => OnDeleteSelected();
        headerRow.Controls.Add(lbl, 0, 0);
        headerRow.Controls.Add(newGroupBtn, 1, 0);
        headerRow.Controls.Add(deleteBtn, 2, 0);

        _groupList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Bg,
            Margin = new Padding(0, 6, 0, 0),
            AllowDrop = true,
        };
        _groupList.DragEnter += OnGroupListDragEnter;
        _groupList.DragOver += OnGroupListDragOver;
        _groupList.DragLeave += OnGroupListDragLeave;
        _groupList.DragDrop += OnGroupListDragDrop;

        pane.Controls.Add(_groupList);
        pane.Controls.Add(headerRow);
        headerRow.Dock = DockStyle.Top;
        return pane;
    }

    private Panel BuildDetailPanel()
    {
        var outer = new Panel { BackColor = Surface, Padding = new Padding(14, 12, 14, 12) };

        _detailHeader = new Label
        {
            Text = "Select a chat, a tab, or a group to see details",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        outer.Controls.Add(_detailHeader);

        // Both cards share the same area; only one is visible at a time.
        _tabCard = BuildTabCard();
        _tabCard.Dock = DockStyle.Fill;
        _tabCard.Visible = false;
        _tabCard.Margin = new Padding(0, 8, 0, 0);

        _groupCard = BuildGroupCard();
        _groupCard.Dock = DockStyle.Fill;
        _groupCard.Visible = false;
        _groupCard.Margin = new Padding(0, 8, 0, 0);

        outer.Controls.Add(_tabCard);
        outer.Controls.Add(_groupCard);
        return outer;
    }

    private Panel BuildTabCard()
    {
        var card = new Panel { BackColor = Surface };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Surface,
            Padding = new Padding(0, 8, 0, 0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        AddField(grid, "Title", _tabTitleBox = MakeTextBox(), 0);
        var colorRow = BuildColorRow(out _tabColorBox);
        AddField(grid, "Color", colorRow, 0, columnStart: 2);
        AddField(grid, "Cwd", _cwdBox = MakeTextBox(), 1, spanCols: 3);
        AddField(grid, "Prompt", _promptBox = MakeComboBox(PromptOptions), 2);
        AddField(grid, "Extra args", _extraArgsBox = MakeComboBox(ExtraArgsOptions), 2, columnStart: 2);
        AddField(grid, "Shell", _shellBox = MakeComboBox(ShellOptions), 3);

        var applyRow = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Surface, Margin = new Padding(0, 8, 0, 0) };
        _tabApplyBtn = MakePrimaryButton("Apply changes");
        _tabApplyBtn.Dock = DockStyle.Right;
        _tabApplyBtn.Width = 150;
        _tabApplyBtn.Click += (_, _) => OnApplyTabDetail();
        applyRow.Controls.Add(_tabApplyBtn);

        card.Controls.Add(applyRow);
        card.Controls.Add(grid);
        return card;
    }

    private Panel BuildGroupCard()
    {
        var card = new Panel { BackColor = Surface };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Surface,
            Padding = new Padding(0, 8, 0, 0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        AddField(grid, "Title", _groupTitleBox = MakeTextBox(), 0);
        var colorRow = BuildColorRow(out _groupColorBox);
        AddField(grid, "Color", colorRow, 0, columnStart: 2);

        var applyRow = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Surface, Margin = new Padding(0, 8, 0, 0) };
        _groupApplyBtn = MakePrimaryButton("Apply changes");
        _groupApplyBtn.Dock = DockStyle.Right;
        _groupApplyBtn.Width = 150;
        _groupApplyBtn.Click += (_, _) => OnApplyGroupDetail();
        applyRow.Controls.Add(_groupApplyBtn);

        card.Controls.Add(applyRow);
        card.Controls.Add(grid);
        return card;
    }

    private Panel BuildColorRow(out TextBox colorBox)
    {
        var row = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        colorBox = MakeTextBox();
        colorBox.Dock = DockStyle.Fill;
        var pickBtn = MakeSecondaryButton("Pick…", bordered: true);
        pickBtn.Dock = DockStyle.Right;
        pickBtn.Width = 64;
        pickBtn.Margin = new Padding(6, 0, 0, 0);
        var captured = colorBox;
        pickBtn.Click += (_, _) => OnPickColor(captured);
        row.Controls.Add(colorBox);
        row.Controls.Add(pickBtn);
        return row;
    }

    private void AddField(TableLayoutPanel grid, string labelText, Control control, int row, int columnStart = 0, int spanCols = 1)
    {
        var lbl = new Label
        {
            Text = labelText + ":",
            Font = new Font("Segoe UI", 9F),
            ForeColor = TextMuted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 6, 0),
            AutoSize = false,
            Height = 30,
        };
        grid.Controls.Add(lbl, columnStart, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 8, 4);
        grid.Controls.Add(control, columnStart + 1, row);
        if (spanCols > 1) grid.SetColumnSpan(control, spanCols);
        while (grid.RowStyles.Count <= row)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
    }

    // ====== Data refresh ======

    private void RefreshChats()
    {
        _chats = ChatScanner.DiscoverAll();
        PopulateChatList();
    }

    private void PopulateChatList()
    {
        _chatList.SuspendLayout();
        _chatList.Controls.Clear();
        if (_chats.Count == 0)
        {
            _chatList.Controls.Add(EmptyLabel("No chats found under ~/.claude/projects/"));
        }
        else
        {
            var visible = ApplyChatFilter(_chats, _chatFilter);
            // Drop the selection if the active filter hides it — otherwise "Add to group" still
            // operates on a chat the user can no longer see, which is confusing.
            if (_selectedChat is not null && !visible.Any(c => c.SessionId == _selectedChat.SessionId))
                _selectedChat = null;
            if (visible.Count == 0)
            {
                _chatList.Controls.Add(EmptyLabel($"No chats match \"{_chatFilter}\"."));
            }
            else
            {
                foreach (var chat in visible)
                {
                    var item = new ChatItem(chat, GroupsContainingSession(chat.SessionId))
                    {
                        Width = _chatList.ClientSize.Width - 24,
                    };
                    item.OnClicked += () => SelectChat(chat);
                    _chatList.Controls.Add(item);
                }
                ReapplyChatSelection();
            }
        }
        _chatList.ResumeLayout();
    }

    private static IReadOnlyList<ChatRecord> ApplyChatFilter(IReadOnlyList<ChatRecord> chats, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return chats;
        return chats.Where(c =>
            c.SessionId.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || c.WorkingDir.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || c.Preview.Contains(filter, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    private int GroupsContainingSession(string sessionId)
    {
        var tabIds = _tabs.Where(t => t.SessionId == sessionId).Select(t => t.Id).ToHashSet();
        return _groups.Count(g => g.TabIds.Any(id => tabIds.Contains(id)));
    }

    private void RefreshGroups()
    {
        // Preserve scroll position across the rebuild — without this, dropping a tab near the
        // bottom of a long list snaps the view back to the top.
        var savedScroll = _groupList.AutoScrollPosition;
        _groupList.SuspendLayout();
        _groupList.Controls.Clear();
        if (_groups.Count == 0)
        {
            _groupList.Controls.Add(EmptyLabel("No groups yet.\nClick '+ Group' to create one."));
        }
        else
        {
            foreach (var grp in _groups)
            {
                var header = new GroupHeader(grp) { Width = _groupList.ClientSize.Width - 24 };
                header.OnClicked += () => SelectGroup(grp);
                header.OnDragInitiate += payload => header.DoDragDrop(new DataObject(typeof(DragPayload).FullName!, payload), DragDropEffects.Move);
                _groupList.Controls.Add(header);

                foreach (var tabId in grp.TabIds)
                {
                    var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                    if (tab is null) continue;
                    var row = new TabRow(tab, grp.Id) { Width = _groupList.ClientSize.Width - 24 };
                    row.OnClicked += () => SelectTab(tab);
                    row.OnRemoveClicked += () => OnRemoveTabFromGroup(grp, tabId);
                    row.OnDragInitiate += payload => row.DoDragDrop(new DataObject(typeof(DragPayload).FullName!, payload), DragDropEffects.Move);
                    _groupList.Controls.Add(row);
                }
            }
            ReapplyItemSelection();
        }
        _groupList.ResumeLayout();
        // AutoScrollPosition reads as a negative point but writes as positive — flip the sign.
        _groupList.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
    }

    private Label EmptyLabel(string text) => new()
    {
        Text = text,
        ForeColor = TextMuted,
        AutoSize = false,
        Size = new Size(_chatList.ClientSize.Width - 4, 70),
        TextAlign = ContentAlignment.MiddleCenter,
    };

    // ====== Selection (decoupled) ======

    private void SelectChat(ChatRecord chat)
    {
        _selectedChat = chat;
        ReapplyChatSelection();
        PopulateDetailFromChat(chat);
    }

    private void SelectTab(TabPreset tab)
    {
        _selectedItem = tab;
        ReapplyItemSelection();
        PopulateDetailFromTab(tab);
    }

    private void SelectGroup(GroupPreset grp)
    {
        _selectedItem = grp;
        ReapplyItemSelection();
        PopulateDetailFromGroup(grp);
    }

    private void ReapplyChatSelection()
    {
        foreach (Control c in _chatList.Controls)
            if (c is ChatItem ci) ci.IsSelected = _selectedChat is not null && ci.SessionId == _selectedChat.SessionId;
    }

    private void ReapplyItemSelection()
    {
        foreach (Control c in _groupList.Controls)
        {
            if (c is GroupHeader gh)
                gh.IsSelected = _selectedItem is GroupPreset g && gh.GroupId == g.Id;
            else if (c is TabRow tr)
                tr.IsSelected = _selectedItem is TabPreset t && tr.TabId == t.Id;
        }
    }

    // ====== Detail population ======

    private void PopulateDetailFromChat(ChatRecord chat)
    {
        _detailHeader.Text = $"Chat preview — {chat.ShortId}  ·  {chat.WorkingDir}";
        var folder = Path.GetFileName(chat.WorkingDir.TrimEnd('\\', '/'));
        _tabTitleBox.Text = string.IsNullOrEmpty(folder) ? chat.WorkingDir : folder;
        _tabColorBox.Text = string.Empty;
        _cwdBox.Text = chat.WorkingDir;
        _promptBox.Text = string.Empty;
        _extraArgsBox.Text = string.Empty;
        _shellBox.Text = string.Empty;
        _tabApplyBtn.Enabled = false; // chat is a preview, not yet a tab — use "Add to group"
        ShowTabCard();
    }

    private void PopulateDetailFromTab(TabPreset tab)
    {
        var refCount = _groups.Count(g => g.TabIds.Contains(tab.Id));
        _detailHeader.Text = $"Tab — {tab.Title}  ·  in {refCount} group(s)  ·  session {tab.SessionId.Substring(0, Math.Min(8, tab.SessionId.Length))}…";
        _tabTitleBox.Text = tab.Title;
        _tabColorBox.Text = tab.TabColor ?? string.Empty;
        _cwdBox.Text = tab.WorkingDir;
        _promptBox.Text = tab.InitialPrompt ?? string.Empty;
        _extraArgsBox.Text = string.Join(' ', tab.ExtraClaudeArgs);
        _shellBox.Text = tab.Shell ?? string.Empty;
        _tabApplyBtn.Enabled = true;
        ShowTabCard();
    }

    private void PopulateDetailFromGroup(GroupPreset grp)
    {
        _detailHeader.Text = $"Group — {grp.Title}  ·  {grp.TabIds.Count} tab(s)";
        _groupTitleBox.Text = grp.Title;
        _groupColorBox.Text = grp.Color ?? string.Empty;
        _groupApplyBtn.Enabled = true;
        ShowGroupCard();
    }

    private void ClearDetail()
    {
        _detailHeader.Text = "Select a chat, a tab, or a group to see details";
        _tabTitleBox.Text = _tabColorBox.Text = _cwdBox.Text = _promptBox.Text = _extraArgsBox.Text = _shellBox.Text = string.Empty;
        _groupTitleBox.Text = _groupColorBox.Text = string.Empty;
        _tabApplyBtn.Enabled = false;
        _groupApplyBtn.Enabled = false;
        HideAllCards();
    }

    private void ShowTabCard()
    {
        _tabCard.Visible = true;
        _groupCard.Visible = false;
        _tabCard.BringToFront();
    }

    private void ShowGroupCard()
    {
        _groupCard.Visible = true;
        _tabCard.Visible = false;
        _groupCard.BringToFront();
    }

    private void HideAllCards()
    {
        _tabCard.Visible = false;
        _groupCard.Visible = false;
    }

    // ====== Apply / add / remove / delete ======

    private void OnApplyTabDetail()
    {
        if (_selectedItem is not TabPreset tab)
        {
            SetStatus("Click a tab in a group on the right to edit it.", true);
            return;
        }
        var updated = tab with
        {
            Title = _tabTitleBox.Text.Trim(),
            TabColor = string.IsNullOrWhiteSpace(_tabColorBox.Text) ? null : _tabColorBox.Text.Trim(),
            WorkingDir = _cwdBox.Text.Trim(),
            InitialPrompt = string.IsNullOrWhiteSpace(_promptBox.Text) ? null : _promptBox.Text.Trim(),
            ExtraClaudeArgs = SplitArgs(_extraArgsBox.Text),
            Shell = string.IsNullOrWhiteSpace(_shellBox.Text) ? null : _shellBox.Text.Trim(),
        };
        var idx = _tabs.FindIndex(t => t.Id == tab.Id);
        if (idx >= 0) _tabs[idx] = updated;
        _selectedItem = updated;
        RefreshGroups();
        SetStatus($"Tab '{updated.Title}' updated.", false);
    }

    private void OnApplyGroupDetail()
    {
        if (_selectedItem is not GroupPreset grp)
        {
            SetStatus("Click a group header on the right to edit it.", true);
            return;
        }
        var updated = grp with
        {
            Title = _groupTitleBox.Text.Trim(),
            Color = string.IsNullOrWhiteSpace(_groupColorBox.Text) ? null : _groupColorBox.Text.Trim(),
        };
        var idx = _groups.FindIndex(g => g.Id == grp.Id);
        if (idx >= 0) _groups[idx] = updated;
        _selectedItem = updated;
        RefreshGroups();
        SetStatus($"Group '{updated.Title}' updated.", false);
    }

    private void OnPickColor(TextBox target)
    {
        using var dlg = new ColorDialog
        {
            FullOpen = true,
            Color = TryParseHex(target.Text) ?? Color.Gray,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            target.Text = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
    }

    private void OnAddChatToGroup()
    {
        if (_selectedChat is null)
        {
            SetStatus("Pick a chat from the left list first.", true);
            return;
        }
        if (_selectedItem is not GroupPreset selectedGroup)
        {
            SetStatus("Pick a group (click its header) on the right list first.", true);
            return;
        }

        // Refuse to add the same chat twice to the same group.
        var alreadyInGroup = selectedGroup.TabIds
            .Select(id => _tabs.FirstOrDefault(t => t.Id == id))
            .Any(t => t is not null && t.SessionId == _selectedChat.SessionId);
        if (alreadyInGroup)
        {
            SetStatus($"This chat is already in '{selectedGroup.Title}'.", true);
            return;
        }

        // Always create a fresh tab — no reuse across groups. Each tab has its own settings,
        // so editing the title/color in one group doesn't bleed into another, and selecting it
        // doesn't highlight the same chat in other groups.
        // Defaults are derived from the chat (cwd, folder-name title); nothing is hardcoded.
        var folder = Path.GetFileName(_selectedChat.WorkingDir.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(folder)) folder = _selectedChat.ShortId;
        var tab = new TabPreset
        {
            Id = $"tab-{Guid.NewGuid().ToString("N")[..8]}",
            SessionId = _selectedChat.SessionId,
            Title = folder,
            WorkingDir = _selectedChat.WorkingDir,
            TabColor = null,
            InitialPrompt = null,
            ExtraClaudeArgs = Array.Empty<string>(),
            Shell = null,
            PreCommands = Array.Empty<string>(),
        };
        _tabs.Add(tab);

        var newTabIds = selectedGroup.TabIds.Append(tab.Id).ToList();
        var gIdx = _groups.FindIndex(g => g.Id == selectedGroup.Id);
        _groups[gIdx] = selectedGroup with { TabIds = newTabIds };
        RefreshGroups();
        RefreshChats();

        // Auto-select the new tab so the user can fill in title/color/prompt right away.
        _selectedItem = tab;
        PopulateDetailFromTab(tab);
        ReapplyItemSelection();
        SetStatus($"Added to '{_groups[gIdx].Title}'. Set the title/color/prompt above and click Apply.", false);
    }

    private void OnRemoveTabFromGroup(GroupPreset grp, string tabId)
    {
        var idx = _groups.FindIndex(g => g.Id == grp.Id);
        if (idx < 0) return;
        _groups[idx] = grp with { TabIds = grp.TabIds.Where(id => id != tabId).ToList() };
        // Track selection: if the user had the group selected, refresh the reference;
        // if they had the just-removed tab selected, clear so we don't show an orphan in the detail.
        if (_selectedItem is GroupPreset g && g.Id == grp.Id) _selectedItem = _groups[idx];
        else if (_selectedItem is TabPreset t && t.Id == tabId) { _selectedItem = null; ClearDetail(); }
        RefreshGroups();
        RefreshChats();
        SetStatus("Tab removed from group.", false);
    }

    private void OnNewGroup()
    {
        var id = $"group-{Guid.NewGuid().ToString("N")[..8]}";
        var grp = new GroupPreset
        {
            Id = id,
            Title = "New group",
            Color = "#888888",
            TabIds = Array.Empty<string>(),
        };
        _groups.Add(grp);
        _selectedItem = grp;
        RefreshGroups();
        PopulateDetailFromGroup(grp);
        SetStatus("New group created. Edit its title above.", false);
    }

    private void OnDeleteSelected()
    {
        if (_selectedItem is GroupPreset grp)
        {
            _groups.RemoveAll(g => g.Id == grp.Id);
            _selectedItem = null;
            ClearDetail();
            RefreshGroups();
            RefreshChats();
            SetStatus($"Group '{grp.Title}' deleted.", false);
        }
        else if (_selectedItem is TabPreset tab)
        {
            var refCount = _groups.Count(g => g.TabIds.Contains(tab.Id));
            var msg = refCount > 1
                ? $"Delete tab '{tab.Title}' globally? It's in {refCount} groups — use the '×' on a row to remove from one group only."
                : $"Delete tab '{tab.Title}'?";
            if (MessageBox.Show(this, msg, "Confirm delete", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            _tabs.RemoveAll(t => t.Id == tab.Id);
            for (int i = 0; i < _groups.Count; i++)
                _groups[i] = _groups[i] with { TabIds = _groups[i].TabIds.Where(id => id != tab.Id).ToList() };
            _selectedItem = null;
            ClearDetail();
            RefreshGroups();
            RefreshChats();
            SetStatus($"Tab '{tab.Title}' deleted globally.", false);
        }
        else
        {
            SetStatus("Select a group header or a tab row first.", true);
        }
    }

    private void SaveAndClose()
    {
        var newConfig = new PresetsConfig
        {
            SchemaVersion = PresetsConfig.CurrentSchemaVersion,
            Settings = _settings,
            Tabs = _tabs,
            Groups = _groups,
        };
        try
        {
            ConfigStore.Save(newConfig);
            SavedChanges = true;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}", true);
        }
    }

    // ====== Drag & drop reorder ======

    // Payload travels through DataObject as the only token of a drag — kind tells us what we're
    // dragging, OriginGroupId is required for tabs (so we know where to remove from).
    private sealed record DragPayload(string Kind, string Id, string? OriginGroupId);

    private void OnGroupListDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetData(typeof(DragPayload)) is DragPayload
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void OnGroupListDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(DragPayload)) is not DragPayload payload)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        var clientPt = _groupList.PointToClient(new Point(e.X, e.Y));
        var slot = ResolveDropSlot(payload, clientPt);
        ClearAllInsertionIndicators();
        if (slot is null)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        slot.Value.PaintHint();
        e.Effect = DragDropEffects.Move;
    }

    private void OnGroupListDragLeave(object? sender, EventArgs e) => ClearAllInsertionIndicators();

    private void OnGroupListDragDrop(object? sender, DragEventArgs e)
    {
        ClearAllInsertionIndicators();
        if (e.Data?.GetData(typeof(DragPayload)) is not DragPayload payload) return;

        var clientPt = _groupList.PointToClient(new Point(e.X, e.Y));
        var slot = ResolveDropSlot(payload, clientPt);
        if (slot is null) return;

        if (payload.Kind == "group")
            ApplyGroupMove(payload.Id, slot.Value.GroupTargetIndex!.Value);
        else
            ApplyTabMove(payload.Id, payload.OriginGroupId!, slot.Value.TabTargetGroupId!, slot.Value.TabTargetIndex!.Value);
    }

    // A resolved drop slot. For group drags we know an index into _groups. For tab drags we
    // know the destination group + the index within its TabIds. PaintHint sets the insertion
    // line on the right child for visual feedback during DragOver.
    private readonly struct DropSlot
    {
        public int? GroupTargetIndex { get; init; }
        public string? TabTargetGroupId { get; init; }
        public int? TabTargetIndex { get; init; }
        public Action PaintHint { get; init; }
    }

    private DropSlot? ResolveDropSlot(DragPayload payload, Point clientPt)
    {
        var children = _groupList.Controls.Cast<Control>().ToList();
        if (children.Count == 0) return null;

        // Find the child the cursor is over (within its vertical extent).
        Control? hit = null;
        bool topHalf = false;
        foreach (var c in children)
        {
            if (clientPt.Y >= c.Top && clientPt.Y < c.Bottom)
            {
                hit = c;
                topHalf = clientPt.Y < c.Top + c.Height / 2;
                break;
            }
        }

        // Above the first child or below the last child — snap to the closest end.
        if (hit is null)
        {
            if (clientPt.Y < children[0].Top) { hit = children[0]; topHalf = true; }
            else { hit = children[^1]; topHalf = false; }
        }

        if (payload.Kind == "group")
            return ResolveGroupDrop(payload, hit, topHalf, children);
        return ResolveTabDrop(payload, hit, topHalf, children);
    }

    private DropSlot? ResolveGroupDrop(DragPayload payload, Control hit, bool topHalf, List<Control> children)
    {
        // Find the group context for the hit row (a TabRow belongs to the most recent GroupHeader above it).
        var hitGroupId = HitGroupId(hit, children);
        if (hitGroupId is null) return null;
        var hitIdx = _groups.FindIndex(g => g.Id == hitGroupId);
        if (hitIdx < 0) return null;

        // For group reorder we land before the hit group (top half) or after it (bottom half),
        // regardless of whether the hovered row was the header or one of its tabs.
        var targetIdx = topHalf ? hitIdx : hitIdx + 1;
        var srcIdx = _groups.FindIndex(g => g.Id == payload.Id);
        if (srcIdx < 0) return null;
        if (targetIdx == srcIdx || targetIdx == srcIdx + 1) return null; // no-op move

        // Insertion line goes above the target group's header (or below the last group's last child).
        var hintTarget = FindGroupBoundaryControl(targetIdx, children);
        var paintAtTop = targetIdx < _groups.Count;
        return new DropSlot
        {
            GroupTargetIndex = targetIdx,
            PaintHint = () =>
            {
                if (hintTarget is GroupHeader gh) gh.InsertionLineAtTop = true;
                else if (hintTarget is TabRow tr) tr.InsertionLineAtBottom = !paintAtTop;
            },
        };
    }

    private DropSlot? ResolveTabDrop(DragPayload payload, Control hit, bool topHalf, List<Control> children)
    {
        var hitGroupId = HitGroupId(hit, children);
        if (hitGroupId is null) return null;
        var grp = _groups.FirstOrDefault(g => g.Id == hitGroupId);
        if (grp is null) return null;

        // Index within the target group's TabIds where the dropped tab will be inserted.
        int destIdx;
        if (hit is GroupHeader)
        {
            // Dropping on a group header → first slot in that group.
            destIdx = 0;
        }
        else if (hit is TabRow tr)
        {
            var tabIdxInGroup = grp.TabIds.ToList().IndexOf(tr.TabId);
            if (tabIdxInGroup < 0) return null;
            destIdx = topHalf ? tabIdxInGroup : tabIdxInGroup + 1;
        }
        else return null;

        // No-op detection: dropping a tab back where it already is (only when the source
        // and destination groups match).
        if (hitGroupId == payload.OriginGroupId)
        {
            var srcIdx = grp.TabIds.ToList().IndexOf(payload.Id);
            if (srcIdx == destIdx || srcIdx == destIdx - 1) return null;
        }
        else if (grp.TabIds.Contains(payload.Id))
        {
            // Cross-group move into a group that already contains this tab — refuse so we
            // don't end up with a duplicated id in the destination group.
            return null;
        }

        return new DropSlot
        {
            TabTargetGroupId = hitGroupId,
            TabTargetIndex = destIdx,
            PaintHint = () =>
            {
                if (hit is GroupHeader gh) gh.InsertionLineAtBottom = true;
                else if (hit is TabRow tr2)
                {
                    if (topHalf) tr2.InsertionLineAtTop = true;
                    else tr2.InsertionLineAtBottom = true;
                }
            },
        };
    }

    // For a TabRow, find the GroupId of the group it belongs to (last GroupHeader above it).
    // For a GroupHeader, just return its own GroupId.
    private static string? HitGroupId(Control hit, List<Control> children)
    {
        if (hit is GroupHeader gh) return gh.GroupId;
        if (hit is TabRow)
        {
            var idx = children.IndexOf(hit);
            for (int i = idx - 1; i >= 0; i--)
                if (children[i] is GroupHeader g) return g.GroupId;
        }
        return null;
    }

    // Returns the control at which to paint the insertion line for a group target index.
    // For a target index INSIDE the list, that's the GroupHeader at that index. For an index
    // beyond the last group, it's the LAST control in the panel (so the line shows at its bottom).
    private Control? FindGroupBoundaryControl(int targetGroupIndex, List<Control> children)
    {
        if (targetGroupIndex >= _groups.Count) return children.Count == 0 ? null : children[^1];
        var targetGroupId = _groups[targetGroupIndex].Id;
        return children.OfType<GroupHeader>().FirstOrDefault(gh => gh.GroupId == targetGroupId);
    }

    private void ClearAllInsertionIndicators()
    {
        foreach (Control c in _groupList.Controls)
        {
            if (c is GroupHeader gh) { gh.InsertionLineAtTop = gh.InsertionLineAtBottom = false; }
            else if (c is TabRow tr) { tr.InsertionLineAtTop = tr.InsertionLineAtBottom = false; }
        }
    }

    private void ApplyGroupMove(string groupId, int targetIndex)
    {
        var srcIdx = _groups.FindIndex(g => g.Id == groupId);
        if (srcIdx < 0) return;
        var moving = _groups[srcIdx];
        _groups.RemoveAt(srcIdx);
        if (targetIndex > srcIdx) targetIndex--; // compensate for the just-removed slot
        targetIndex = Math.Clamp(targetIndex, 0, _groups.Count);
        _groups.Insert(targetIndex, moving);
        _selectedItem = moving;
        RefreshGroups();
        SetStatus($"Group '{moving.Title}' moved.", false);
    }

    private void ApplyTabMove(string tabId, string fromGroupId, string toGroupId, int destIndex)
    {
        var fromIdx = _groups.FindIndex(g => g.Id == fromGroupId);
        var toIdx = _groups.FindIndex(g => g.Id == toGroupId);
        if (fromIdx < 0 || toIdx < 0) return;

        var fromTabs = _groups[fromIdx].TabIds.ToList();
        var srcSlot = fromTabs.IndexOf(tabId);
        if (srcSlot < 0) return;
        fromTabs.RemoveAt(srcSlot);
        _groups[fromIdx] = _groups[fromIdx] with { TabIds = fromTabs };

        // Refetch the destination list AFTER mutating source — same group case shares the list.
        var toTabs = _groups[toIdx].TabIds.ToList();
        if (fromGroupId == toGroupId && srcSlot < destIndex) destIndex--;
        destIndex = Math.Clamp(destIndex, 0, toTabs.Count);
        toTabs.Insert(destIndex, tabId);
        _groups[toIdx] = _groups[toIdx] with { TabIds = toTabs };

        var movedTab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (movedTab is not null) _selectedItem = movedTab;
        RefreshGroups();
        SetStatus(fromGroupId == toGroupId ? "Tab reordered." : $"Tab moved to '{_groups[toIdx].Title}'.", false);
    }

    // ====== Helpers ======

    private void SetStatus(string text, bool isError)
    {
        _statusLabel.ForeColor = isError ? StatusError : StatusInfo;
        _statusLabel.Text = text;
    }

    // Splits "--foo 'hello world' --bar baz" → ["--foo", "hello world", "--bar", "baz"].
    private static IReadOnlyList<string> SplitArgs(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuote = false;
        char quoteChar = ' ';

        foreach (var c in input)
        {
            if (inQuote)
            {
                if (c == quoteChar) inQuote = false;
                else current.Append(c);
            }
            else if (c == '"' || c == '\'')
            {
                inQuote = true;
                quoteChar = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { args.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(c);
        }
        if (current.Length > 0) args.Add(current.ToString());
        return args;
    }

    // ====== Editor-specific control factories (textbox/combo use a darker input bg) ======

    private static TextBox MakeTextBox() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = InputBg,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9F),
    };

    // Editable dropdown — user can pick from the list OR type freely. Empty first item =
    // explicit "blank" choice (no startup prompt / use default shell / no extra args).
    private static ComboBox MakeComboBox(string[] items)
    {
        var cb = new ComboBox
        {
            Dock = DockStyle.Fill,
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F),
            DropDownStyle = ComboBoxStyle.DropDown,
        };
        cb.Items.AddRange(items);
        return cb;
    }

    // ====== Custom item controls (decoupled selection) ======

    private interface ISelectableItem { bool IsSelected { get; set; } }

    private sealed class ChatItem : Panel, ISelectableItem
    {
        private readonly ChatRecord _chat;
        private readonly int _groupCount;
        private bool _hovered, _selected;
        public event Action? OnClicked;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsSelected { get => _selected; set { _selected = value; Invalidate(); } }
        public string SessionId => _chat.SessionId;

        public ChatItem(ChatRecord chat, int groupCount)
        {
            _chat = chat;
            _groupCount = groupCount;
            Height = 72;
            BackColor = Surface;
            Margin = new Padding(0, 0, 0, 6);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
            MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
            MouseDown += (_, _) => OnClicked?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var bg = _selected ? SurfaceSelected : (_hovered ? SurfaceHover : Surface);
            g.Clear(bg);

            using var titleFont = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            using var idFont = new Font("Consolas", 8.5F);
            using var bodyFont = new Font("Segoe UI", 9F);
            using var smallFont = new Font("Segoe UI", 8F);

            // Line 1: project folder short name + short id
            var folderName = Path.GetFileName(_chat.WorkingDir.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(folderName)) folderName = _chat.WorkingDir;
            TextRenderer.DrawText(g, folderName, titleFont,
                new Rectangle(12, 6, Width - 90, 20), TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _chat.ShortId, idFont,
                new Rectangle(Width - 75, 8, 60, 16), TextDim,
                TextFormatFlags.Right);

            // Line 2: last message preview (most recent user OR assistant text)
            TextRenderer.DrawText(g, _chat.Preview, bodyFont,
                new Rectangle(12, 26, Width - 24, 18), TextMuted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            // Line 3: date + optional 'in N groups' badge
            TextRenderer.DrawText(g, _chat.LastModified.ToString("yyyy-MM-dd HH:mm"), smallFont,
                new Rectangle(12, 50, Width - 80, 16), TextDim,
                TextFormatFlags.Left);

            if (_groupCount > 0)
            {
                var badge = $" in {_groupCount} ";
                var sz = TextRenderer.MeasureText(g, badge, smallFont);
                var rect = new Rectangle(Width - sz.Width - 12, Height - 22, sz.Width, 16);
                using var brush = new SolidBrush(AccentBlue);
                g.FillRectangle(brush, rect);
                TextRenderer.DrawText(g, badge, smallFont, rect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    private sealed class GroupHeader : Panel, ISelectableItem
    {
        private readonly GroupPreset _group;
        private bool _hovered, _selected;
        private bool _insertionTop, _insertionBottom;
        private Point _pressPt;
        private bool _pressed;
        private bool _dragging;
        public event Action? OnClicked;
        public event Action<DragPayload>? OnDragInitiate;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsSelected { get => _selected; set { _selected = value; Invalidate(); } }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool InsertionLineAtTop { get => _insertionTop; set { if (_insertionTop != value) { _insertionTop = value; Invalidate(); } } }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool InsertionLineAtBottom { get => _insertionBottom; set { if (_insertionBottom != value) { _insertionBottom = value; Invalidate(); } } }
        public string GroupId => _group.Id;

        public GroupHeader(GroupPreset group)
        {
            _group = group;
            Height = 36;
            BackColor = Surface;
            Margin = new Padding(0, 6, 0, 2);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
            MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
            MouseDown += OnMouseDownInternal;
            MouseMove += OnMouseMoveInternal;
            MouseUp += OnMouseUpInternal;
        }

        private void OnMouseDownInternal(object? s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _pressed = true;
            _dragging = false;
            _pressPt = e.Location;
        }

        private void OnMouseMoveInternal(object? s, MouseEventArgs e)
        {
            if (!_pressed || _dragging) return;
            var dx = Math.Abs(e.X - _pressPt.X);
            var dy = Math.Abs(e.Y - _pressPt.Y);
            if (dx <= SystemInformation.DragSize.Width && dy <= SystemInformation.DragSize.Height) return;
            _dragging = true;
            OnDragInitiate?.Invoke(new DragPayload("group", _group.Id, null));
            _pressed = false;
        }

        private void OnMouseUpInternal(object? s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_pressed && !_dragging) OnClicked?.Invoke();
            _pressed = false;
            _dragging = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var bg = _selected ? SurfaceSelected : (_hovered ? SurfaceHover : Surface);
            g.Clear(bg);

            var accent = TryParseHex(_group.Color) ?? Color.Gray;
            using (var brush = new SolidBrush(accent))
                g.FillRectangle(brush, 0, 0, 4, Height);

            using var titleFont = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            TextRenderer.DrawText(g, _group.Title, titleFont,
                new Rectangle(14, 0, Width - 100, Height), TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            using var smallFont = new Font("Segoe UI", 8F);
            TextRenderer.DrawText(g, $"{_group.TabIds.Count} tab(s)", smallFont,
                new Rectangle(Width - 80, 0, 70, Height), TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);

            using var indPen = new Pen(AccentBlue, 2);
            if (_insertionTop) g.DrawLine(indPen, 0, 1, Width, 1);
            if (_insertionBottom) g.DrawLine(indPen, 0, Height - 1, Width, Height - 1);
        }
    }

    private sealed class TabRow : Panel, ISelectableItem
    {
        private readonly TabPreset _tab;
        private readonly string _ownerGroupId;
        private bool _hovered, _selected;
        private bool _insertionTop, _insertionBottom;
        private Point _pressPt;
        private bool _pressed;
        private bool _dragging;
        public event Action? OnRemoveClicked;
        public event Action? OnClicked;
        public event Action<DragPayload>? OnDragInitiate;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsSelected { get => _selected; set { _selected = value; Invalidate(); } }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool InsertionLineAtTop { get => _insertionTop; set { if (_insertionTop != value) { _insertionTop = value; Invalidate(); } } }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool InsertionLineAtBottom { get => _insertionBottom; set { if (_insertionBottom != value) { _insertionBottom = value; Invalidate(); } } }
        public string TabId => _tab.Id;
        public string OwnerGroupId => _ownerGroupId;

        public TabRow(TabPreset tab, string ownerGroupId)
        {
            _tab = tab;
            _ownerGroupId = ownerGroupId;
            Height = 30;
            BackColor = Bg;
            Margin = new Padding(20, 0, 0, 2);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
            MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
            MouseDown += OnMouseDownInternal;
            MouseMove += OnMouseMoveInternal;
            MouseUp += OnMouseUpInternal;

            var removeBtn = new Button
            {
                Text = "×",
                Width = 28,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Bg,
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            };
            removeBtn.FlatAppearance.BorderSize = 0;
            removeBtn.FlatAppearance.MouseOverBackColor = AccentRed;
            removeBtn.Click += (_, _) => OnRemoveClicked?.Invoke();
            Controls.Add(removeBtn);
        }

        private void OnMouseDownInternal(object? s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _pressed = true;
            _dragging = false;
            _pressPt = e.Location;
        }

        private void OnMouseMoveInternal(object? s, MouseEventArgs e)
        {
            if (!_pressed || _dragging) return;
            var dx = Math.Abs(e.X - _pressPt.X);
            var dy = Math.Abs(e.Y - _pressPt.Y);
            if (dx <= SystemInformation.DragSize.Width && dy <= SystemInformation.DragSize.Height) return;
            _dragging = true;
            OnDragInitiate?.Invoke(new DragPayload("tab", _tab.Id, _ownerGroupId));
            _pressed = false;
        }

        private void OnMouseUpInternal(object? s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_pressed && !_dragging) OnClicked?.Invoke();
            _pressed = false;
            _dragging = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var bg = _selected ? SurfaceSelected : (_hovered ? SurfaceHover : Bg);
            g.Clear(bg);

            var accent = TryParseHex(_tab.TabColor) ?? Color.Gray;
            using (var brush = new SolidBrush(accent))
                g.FillEllipse(brush, 8, Height / 2 - 4, 8, 8);

            using var font = new Font("Segoe UI", 9F);
            TextRenderer.DrawText(g, _tab.Title, font,
                new Rectangle(22, 0, Width - 54, Height), TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            using var indPen = new Pen(AccentBlue, 2);
            if (_insertionTop) g.DrawLine(indPen, 0, 1, Width, 1);
            if (_insertionBottom) g.DrawLine(indPen, 0, Height - 1, Width, Height - 1);
        }
    }
}
