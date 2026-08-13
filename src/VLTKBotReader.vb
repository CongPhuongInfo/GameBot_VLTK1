Imports Microsoft.VisualBasic
Imports System
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports System.Diagnostics
Imports System.Windows.Forms
Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Threading
Imports System.Threading.Tasks
Imports OpenCvSharp
Imports Tesseract

' =============================================================================
'  VLTKBotReader.vb  -  Bot Võ Lâm Truyền Kỳ 1 (VNG client)
'  Kien truc chung voi WC3BotReader / LoLBotReader:
'    - LockBits pixel scan: HP bar do, MP bar xanh
'    - OCR Tesseract: toa do XY, ten map
'    - OpenCV template matching: detect mob/NPC/item drop
'    - Auto-attack: click mob gan nhat + giu phim skill (1-6)
'    - Auto-pickup: F key hoac click icon item roi
'    - Record & Replay farming route (JSON profile)
'    - Input: RealMouse / PostMessage / SendMessage
'
'  VLTK1 VNG HUD 800x600 (mac dinh):
'    HP  bar : (90,  556, 120, 9)  <- thanh do duoi trai
'    MP  bar : (90,  568, 120, 7)  <- thanh xanh
'    Toa do  : (722,  10,  70, 14) <- OCR goc tren phai (minimap label)
'    Ten map : (280,   4, 240, 14) <- OCR giua tren
'    Kinh nghiem bar: (180, 584, 440, 6) <- thanh vang day duoi
' =============================================================================

Public Class VLTKBot
    Inherits Form

#Region "Windows API"
    <DllImport("user32.dll")>
    Private Shared Function GetWindowRect(hWnd As IntPtr, ByRef rc As RECT) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function GetClientRect(hWnd As IntPtr, ByRef rc As RECT) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function ClientToScreen(hWnd As IntPtr, ByRef pt As POINT) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowsHookEx(idHook As Integer, lpfn As LowLevelProc,
                                              hMod As IntPtr, dwThreadId As UInteger) As IntPtr
    End Function
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function UnhookWindowsHookEx(hhk As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function CallNextHookEx(hhk As IntPtr, nCode As Integer,
                                            wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function
    <DllImport("kernel32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function GetModuleHandle(lpModuleName As String) As IntPtr
    End Function
    <DllImport("user32.dll")>
    Private Shared Function SendMessage(hWnd As IntPtr, Msg As UInteger,
                                         wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function
    <DllImport("user32.dll")>
    Private Shared Function PostMessage(hWnd As IntPtr, Msg As UInteger,
                                         wParam As IntPtr, lParam As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function GetCursorPos(ByRef pt As POINT) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function SetCursorPos(x As Integer, y As Integer) As Boolean
    End Function

    Private Const INPUT_MOUSE    As Integer = 0
    Private Const INPUT_KEYBOARD As Integer = 1
    <StructLayout(LayoutKind.Sequential)>
    Private Structure MOUSEINPUT
        Public dx, dy      As Integer
        Public mouseData   As UInteger
        Public dwFlags     As UInteger
        Public time        As UInteger
        Public dwExtraInfo As IntPtr
    End Structure
    <StructLayout(LayoutKind.Sequential)>
    Private Structure KEYBDINPUT
        Public wVk         As UShort
        Public wScan       As UShort
        Public dwFlags     As UInteger
        Public time        As UInteger
        Public dwExtraInfo As IntPtr
    End Structure
    <StructLayout(LayoutKind.Explicit)>
    Private Structure INPUT
        <FieldOffset(0)> Public type As Integer
        <FieldOffset(4)> Public mi   As MOUSEINPUT
        <FieldOffset(4)> Public ki   As KEYBDINPUT
    End Structure
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SendInput(nInputs As UInteger, pInputs() As INPUT, cbSize As Integer) As UInteger
    End Function

    Private Const WH_MOUSE_LL    As Integer = 14
    Private Const WH_KEYBOARD_LL As Integer = 13
    Private Const WM_LBUTTONDOWN As UInteger = &H201
    Private Const WM_LBUTTONUP   As UInteger = &H202
    Private Const WM_RBUTTONDOWN As UInteger = &H204
    Private Const WM_RBUTTONUP   As UInteger = &H205
    Private Const WM_KEYDOWN     As UInteger = &H100
    Private Const WM_KEYUP       As UInteger = &H101
    Private Const MOUSEEVENTF_LEFTDOWN  As UInteger = &H2
    Private Const MOUSEEVENTF_LEFTUP    As UInteger = &H4
    Private Const MOUSEEVENTF_RIGHTDOWN As UInteger = &H8
    Private Const MOUSEEVENTF_RIGHTUP   As UInteger = &H10
    Private Const KEYEVENTF_KEYUP       As UInteger = &H2

    Private Delegate Function LowLevelProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr

    <StructLayout(LayoutKind.Sequential)>
    Public Structure RECT
        Public Left, Top, Right, Bottom As Integer
    End Structure
    <StructLayout(LayoutKind.Sequential)>
    Public Structure POINT
        Public X, Y As Integer
    End Structure
    <StructLayout(LayoutKind.Sequential)>
    Public Structure MSLLHOOKSTRUCT
        Public pt          As POINT
        Public mouseData   As UInteger
        Public flags       As UInteger
        Public time        As UInteger
        Public dwExtraInfo As IntPtr
    End Structure
    <StructLayout(LayoutKind.Sequential)>
    Public Structure KBDLLHOOKSTRUCT
        Public vkCode      As UInteger
        Public scanCode    As UInteger
        Public flags       As UInteger
        Public time        As UInteger
        Public dwExtraInfo As IntPtr
    End Structure
#End Region

#Region "Data Models"
    Public Enum InputMode
        RealMouse
        PostMsg
        SendMsg
    End Enum

    Public Enum ActionType
        MouseMove
        MouseLeftClick
        MouseRightClick
        KeyPress
        Delay
        UseSkill        ' 1-6 skill hotkey
        Pickup          ' F key hoac click item
        Talk            ' click NPC
    End Enum

    Public Class RecordedAction
        <JsonPropertyName("type")>      Public Property Type      As ActionType
        <JsonPropertyName("x")>         Public Property X         As Integer
        <JsonPropertyName("y")>         Public Property Y         As Integer
        <JsonPropertyName("keyCode")>   Public Property KeyCode   As Integer
        <JsonPropertyName("keyName")>   Public Property KeyName   As String = ""
        <JsonPropertyName("delayMs")>   Public Property DelayMs   As Long
        <JsonPropertyName("timestamp")> Public Property Timestamp As Long
        <JsonPropertyName("comment")>   Public Property Comment   As String = ""
    End Class

    Public Class ReplayFile
        <JsonPropertyName("name")>        Public Property Name        As String = "VLTK Route"
        <JsonPropertyName("gameProcess")> Public Property GameProcess As String = "elementclient"
        <JsonPropertyName("mapName")>     Public Property MapName     As String = ""
        <JsonPropertyName("createdAt")>   Public Property CreatedAt   As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        <JsonPropertyName("resolution")>  Public Property Resolution  As String = "800x600"
        <JsonPropertyName("actions")>     Public Property Actions     As New List(Of RecordedAction)()
        <JsonPropertyName("loopCount")>   Public Property LoopCount   As Integer = 1
    End Class

    Public Class DetectResult
        Public Property Label   As String
        Public Property X       As Integer
        Public Property Y       As Integer
        Public Property W       As Integer
        Public Property H       As Integer
        Public Property Score   As Double
        Public Property IsMob   As Boolean   ' True = mob can attack
        Public Property IsItem  As Boolean   ' True = item drop can pickup
    End Class
#End Region

#Region "Constants - VLTK1 VNG HUD 800x600"
    Private Const BASE_W As Integer = 800
    Private Const BASE_H As Integer = 600

    ' HP/MP/NL bar (goc tren trai, HUD nam o DINH man hinh - do anh chup thuc te)
    ' Da do pixel that tu 2 anh chup gameplay (720x540 va 803x605, quy ve chuan 800x600):
    ' thu tu tu trai sang: HP(xanh la) - MP(do) - Noi Luc(xanh duong) - EXP(thanh bac nho)
    Private Const DEF_HP_X As String   = "158"
    Private Const DEF_HP_Y As String   = "8"
    Private Const DEF_HP_W As String   = "104"
    Private Const DEF_HP_H As String   = "10"

    Private Const DEF_MP_X As String   = "268"
    Private Const DEF_MP_Y As String   = "8"
    Private Const DEF_MP_W As String   = "104"
    Private Const DEF_MP_H As String   = "10"

    ' Noi Luc (NL) - thanh xanh duong, ngay sau MP (truoc day bot chua doc thanh nay)
    Private Const DEF_NL_X As String   = "377"
    Private Const DEF_NL_Y As String   = "8"
    Private Const DEF_NL_W As String   = "90"
    Private Const DEF_NL_H As String   = "10"

    ' Kinh nghiem (EXP) - thanh mau bac/xam sang, nho, ngay canh Noi Luc (KHONG phai
    ' thanh vang day cung o duoi man hinh nhu gia dinh truoc day)
    Private Const DEF_EXP_X As String  = "470"
    Private Const DEF_EXP_Y As String  = "8"
    Private Const DEF_EXP_W As String  = "120"
    Private Const DEF_EXP_H As String  = "10"

    ' Toa do nhan vat (OCR - duoi ten vung tren minimap goc tren phai)
    Private Const DEF_COORD_X As String = "670"
    Private Const DEF_COORD_Y As String = "18"
    Private Const DEF_COORD_W As String = "130"
    Private Const DEF_COORD_H As String = "16"

    ' Ten ban do (OCR - phia tren toa do, canh minimap goc tren phai)
    Private Const DEF_MAP_X As String  = "670"
    Private Const DEF_MAP_Y As String  = "0"
    Private Const DEF_MAP_W As String  = "130"
    Private Const DEF_MAP_H As String  = "16"

    ' Vung chup de detect mob/item (phan giua man hinh, tru HUD)
    ' VLTK: HUD tren ~40px (thanh HP/MP/NL/EXP + so lieu duoi thanh), HUD duoi ~120px
    Private Const DETECT_TOP    As Integer = 40
    Private Const DETECT_BOTTOM As Integer = 120   ' cat tu duoi len

    Private Const MOUSE_MOVE_THRESHOLD As Integer = 6

    ' Da do pixel truc tiep tu 2 anh chup gameplay thuc te (goc tren trai man hinh):
    ' Mau HP bar VLTK1: XANH LA  (R<170, G>130, B<110)
    ' Mau MP bar VLTK1: DO       (R>150, G<100, B<100)
    ' Mau Noi Luc (NL) : XANH DUONG (R<110, G<140, B>140)
    ' Mau EXP bar      : BAC/XAM SANG tren nen toi (khong phai vang nhu gia dinh truoc)
    ' Mau ten mob (do) tren dau: R>180 G<80 B<80 (name tag)
    ' Mau item roi: vang sang / trang (tuy item)
    ' Luu y: mau EXP co the lech theo anh sang/nen may chup - dung Pick de lay mau
    ' chinh xac tu client cua ban neu doc sai.
#End Region

#Region "Controls"
    Private lblProcess   As Label
    Private cmbProcess   As ComboBox
    Private btnRefresh   As Button
    Private btnActive    As Button
    Private lblWinInfo   As Label
    Private btnRedetect  As Button

    ' Stat bars
    Private txtHpX  As TextBox, txtHpY  As TextBox, txtHpW  As TextBox, txtHpH  As TextBox, btnPickHp  As Button
    Private txtMpX  As TextBox, txtMpY  As TextBox, txtMpW  As TextBox, txtMpH  As TextBox, btnPickMp  As Button
    Private txtNlX  As TextBox, txtNlY  As TextBox, txtNlW  As TextBox, txtNlH  As TextBox, btnPickNl  As Button
    Private txtExpX As TextBox, txtExpY As TextBox, txtExpW As TextBox, txtExpH As TextBox, btnPickExp As Button
    Private txtCoordX As TextBox, txtCoordY As TextBox, txtCoordW As TextBox, txtCoordH As TextBox, btnPickCoord As Button
    Private txtMapX   As TextBox, txtMapY   As TextBox, txtMapW   As TextBox, txtMapH   As TextBox, btnPickMap   As Button

    ' Stat display
    Private pbHp     As ProgressBar
    Private pbMp     As ProgressBar
    Private pbNl     As ProgressBar
    Private pbExp    As ProgressBar
    Private lblHpVal As Label
    Private lblMpVal As Label
    Private lblNlVal As Label
    Private lblExpVal As Label
    Private lblHpNum  As Label
    Private lblMpNum  As Label
    Private lblNlNum  As Label
    Private lblExpNum As Label
    Private lblCoordVal As Label
    Private lblMapVal   As Label

    ' Auto combat
    Private chkAutoAttack  As CheckBox
    Private chkAutoSkill   As CheckBox
    Private chkAutoPickup  As CheckBox
    Private chkReturnHP    As CheckBox   ' dung danh khi HP thap
    Private nudHpThreshold As NumericUpDown
    Private nudSkillDelay  As NumericUpDown
    Private lstSkillKeys   As CheckedListBox
    Private cmbInputMode   As ComboBox

    ' Detect
    Private chkDetectMob   As CheckBox
    Private chkDetectItem  As CheckBox
    Private chkDetectNPC   As CheckBox
    Private txtTemplateDir As TextBox
    Private btnBrowseTpl   As Button
    Private txtThreshold   As TextBox
    Private lstDetect      As ListBox

    ' Record / Replay
    Private btnRecord       As Button
    Private btnStopRecord   As Button
    Private lblRecordStatus As Label
    Private chkRecordMouse  As CheckBox
    Private chkRecordKeys   As CheckBox
    Private chkRelative     As CheckBox
    Private lstActions      As ListBox
    Private nudLoopCount    As NumericUpDown
    Private btnReplay       As Button
    Private btnStopReplay   As Button
    Private cmbProfiles     As ComboBox
    Private txtJsonPath     As TextBox
    Private btnSaveJson     As Button
    Private btnLoadJson     As Button
    Private btnDeleteProfile As Button

    ' Preview
    Private cmbPreviewZone As ComboBox
    Private picPreview     As PictureBox

    ' Log
    Private txtLog As RichTextBox

    ' Timers
    Private tmrStat   As System.Windows.Forms.Timer
    Private tmrDetect As System.Windows.Forms.Timer
    Private tmrAuto   As System.Windows.Forms.Timer   ' timer rieng cho auto-attack loop
#End Region

#Region "State"
    Private _running        As Boolean = False
    Private _recording      As Boolean = False
    Private _replaying      As Boolean = False
    Private _detectedWinW   As Integer = 0
    Private _detectedWinH   As Integer = 0
    Private _inputMode      As InputMode = InputMode.PostMsg
    Private _threshold      As Double = 0.72
    Private _templateDir    As String = "templates"
    Private _profilesDir    As String = "profiles"
    Private _currentSession As ReplayFile = New ReplayFile()
    Private _lastDetected   As List(Of DetectResult) = New List(Of DetectResult)()
    Private _replayThread   As Thread = Nothing
    Private _detectBusy     As Integer = 0
    Private _autoBusy       As Integer = 0
    Private _mouseHook      As IntPtr = IntPtr.Zero
    Private _keyboardHook   As IntPtr = IntPtr.Zero
    Private _mouseProc      As LowLevelProc
    Private _keyProc        As LowLevelProc
    Private _recordStartMs  As Long = 0
    Private _lastActionMs   As Long = 0
    Private _lastMousePt    As System.Drawing.Point
    Private _pickMode       As String = ""
    Private _pickOverlay    As Form = Nothing
    Private _ocrStatCounter As Integer = 0
    Private _ocrEngine      As TesseractEngine = Nothing
    Private _lastHpPct      As Integer = 100
    Private _lastMpPct      As Integer = 100
    Private _skillIdx       As Integer = 0   ' vong qua danh sach skill
    Private _lastSkillMs    As Long = 0
#End Region

#Region "Constructor"
    Public Sub New()
        Me.Text            = "VLTK Bot Reader"
        Me.Size            = New System.Drawing.Size(1020, 880)
        Me.BackColor       = Color.FromArgb(12, 14, 18)
        Me.ForeColor       = Color.FromArgb(215, 195, 155)
        Me.Font            = New Font("Consolas", 9)
        Me.FormBorderStyle = FormBorderStyle.FixedSingle
        Me.MaximizeBox     = False
        Me.StartPosition   = FormStartPosition.CenterScreen
        Me.Icon            = SystemIcons.Application
        InitUI()
        RefreshProcessList()
        DetectAndApplyWindowSize()
        InitOcr()
        tmrStat             = New System.Windows.Forms.Timer() With {.Interval = 500}
        tmrDetect           = New System.Windows.Forms.Timer() With {.Interval = 400}
        tmrAuto             = New System.Windows.Forms.Timer() With {.Interval = 200}
        AddHandler tmrStat.Tick,   AddressOf OnStatTick
        AddHandler tmrDetect.Tick, AddressOf OnDetectTick
        AddHandler tmrAuto.Tick,   AddressOf OnAutoTick
        LoadProfileList()
        Log("[VLTK Bot] San sang. Chon process roi bam ACTIVE.")
    End Sub
#End Region

#Region "UI Init"
    Private Sub InitUI()
        Dim y As Integer = 8

        ' ── Header ──────────────────────────────────────────────────
        AddLabel("Process:", 8, y + 4, 60)
        cmbProcess = AddCombo(72, y, 190, {"elementclient", "VLTK", "GameClient"})
        AddHandler cmbProcess.SelectedIndexChanged, AddressOf OnProcessSelected

        btnRefresh = MakeBtn("↺", 266, y, 28, 26)
        AddHandler btnRefresh.Click, AddressOf OnRefreshProcess
        Me.Controls.Add(btnRefresh)

        btnActive = MakeBtn("▶  ACTIVE", 298, y, 108, 26)
        btnActive.BackColor = Color.FromArgb(20, 60, 20)
        btnActive.ForeColor = Color.FromArgb(80, 220, 80)
        AddHandler btnActive.Click, AddressOf OnToggleActive
        Me.Controls.Add(btnActive)

        lblWinInfo = New Label() With {.Text = "Window: (chua detect)",
                                        .Location = New System.Drawing.Point(414, y + 5),
                                        .Size = New System.Drawing.Size(260, 16),
                                        .ForeColor = Color.FromArgb(100, 130, 160)}
        Me.Controls.Add(lblWinInfo)

        btnRedetect = MakeBtn("⟳ Re-detect", 682, y, 90, 26)
        AddHandler btnRedetect.Click, AddressOf OnRedetect
        Me.Controls.Add(btnRedetect)

        cmbInputMode = New ComboBox() With {.Location = New System.Drawing.Point(778, y),
                                             .Size = New System.Drawing.Size(118, 26),
                                             .BackColor = Color.FromArgb(28, 30, 38),
                                             .ForeColor = Color.FromArgb(200, 185, 130),
                                             .DropDownStyle = ComboBoxStyle.DropDownList}
        cmbInputMode.Items.AddRange({"RealMouse", "PostMessage", "SendMessage"})
        cmbInputMode.SelectedIndex = 1
        AddHandler cmbInputMode.SelectedIndexChanged, AddressOf OnInputModeChanged
        Me.Controls.Add(cmbInputMode)
        y += 36

        ' ── Vung chup ───────────────────────────────────────────────
        Dim grpCoords As New GroupBox() With {.Text = "Vung chup (X, Y, W, H)  [relative to client]",
                                               .Location = New System.Drawing.Point(8, y),
                                               .Size = New System.Drawing.Size(898, 210),
                                               .ForeColor = Color.FromArgb(140, 130, 100)}
        Me.Controls.Add(grpCoords)

        Dim rows() As (tag As String, lbl As String, defX As String, defY As String, defW As String, defH As String) = {
            ("hp",    "HP  :",    DEF_HP_X,    DEF_HP_Y,    DEF_HP_W,    DEF_HP_H),
            ("mp",    "MP  :",    DEF_MP_X,    DEF_MP_Y,    DEF_MP_W,    DEF_MP_H),
            ("nl",    "NL  :",    DEF_NL_X,    DEF_NL_Y,    DEF_NL_W,    DEF_NL_H),
            ("exp",   "EXP :",    DEF_EXP_X,   DEF_EXP_Y,   DEF_EXP_W,   DEF_EXP_H),
            ("coord", "Toa do :", DEF_COORD_X, DEF_COORD_Y, DEF_COORD_W, DEF_COORD_H),
            ("map",   "Ban do :", DEF_MAP_X,   DEF_MAP_Y,   DEF_MAP_W,   DEF_MAP_H)}

        Dim py As Integer = 18
        For Each row In rows
            Dim lbl As New Label() With {.Text = row.lbl,
                                          .Location = New System.Drawing.Point(8, py + 3),
                                          .Size = New System.Drawing.Size(62, 18)}
            grpCoords.Controls.Add(lbl)
            Dim boxes(3) As TextBox
            Dim defs() As String = {row.defX, row.defY, row.defW, row.defH}
            For j As Integer = 0 To 3
                boxes(j) = New TextBox() With {
                    .Text      = defs(j),
                    .Location  = New System.Drawing.Point(74 + j * 68, py),
                    .Size      = New System.Drawing.Size(62, 22),
                    .BackColor = Color.FromArgb(28, 30, 38),
                    .ForeColor = Color.FromArgb(220, 200, 140),
                    .TextAlign = HorizontalAlignment.Center}
                grpCoords.Controls.Add(boxes(j))
            Next
            Dim btn As Button = MakeBtn("Pick", 350, py - 1, 46, 24)
            btn.ForeColor = Color.FromArgb(80, 180, 255)
            btn.Tag = row.tag
            AddHandler btn.Click, AddressOf OnPickBtn
            grpCoords.Controls.Add(btn)
            ' Gán field
            Select Case row.tag
                Case "hp"    : txtHpX    = boxes(0) : txtHpY    = boxes(1) : txtHpW    = boxes(2) : txtHpH    = boxes(3) : btnPickHp    = btn
                Case "mp"    : txtMpX    = boxes(0) : txtMpY    = boxes(1) : txtMpW    = boxes(2) : txtMpH    = boxes(3) : btnPickMp    = btn
                Case "nl"    : txtNlX    = boxes(0) : txtNlY    = boxes(1) : txtNlW    = boxes(2) : txtNlH    = boxes(3) : btnPickNl    = btn
                Case "exp"   : txtExpX   = boxes(0) : txtExpY   = boxes(1) : txtExpW   = boxes(2) : txtExpH   = boxes(3) : btnPickExp   = btn
                Case "coord" : txtCoordX = boxes(0) : txtCoordY = boxes(1) : txtCoordW = boxes(2) : txtCoordH = boxes(3) : btnPickCoord = btn
                Case "map"   : txtMapX   = boxes(0) : txtMapY   = boxes(1) : txtMapW   = boxes(2) : txtMapH   = boxes(3) : btnPickMap   = btn
            End Select
            py += 28
        Next
        Dim hint As New Label() With {.Text = "* Default coords cho VLTK1 VNG 800x600. Dung Pick de chinh lai theo client cua ban.",
                                       .Location = New System.Drawing.Point(8, py + 2),
                                       .Size = New System.Drawing.Size(700, 16),
                                       .ForeColor = Color.FromArgb(80, 80, 70),
                                       .Font = New Font("Consolas", 7.5)}
        grpCoords.Controls.Add(hint)
        y += 220

        ' ── Chi so ──────────────────────────────────────────────────
        Dim grpStat As New GroupBox() With {.Text = "Chi so nhan vat",
                                             .Location = New System.Drawing.Point(8, y),
                                             .Size = New System.Drawing.Size(898, 96),
                                             .ForeColor = Color.FromArgb(140, 130, 100)}
        Me.Controls.Add(grpStat)

        ' HP
        AddStatRow(grpStat, "HP",  8,   16, Color.FromArgb(60, 200, 60),  pbHp,  lblHpVal,  lblHpNum)
        ' MP
        AddStatRow(grpStat, "MP",  290, 16, Color.FromArgb(220, 60, 60), pbMp,  lblMpVal,  lblMpNum)
        ' Noi Luc
        AddStatRow(grpStat, "NL",  8,   46, Color.FromArgb(60, 120, 255), pbNl,  lblNlVal,  lblNlNum)
        ' EXP
        AddStatRow(grpStat, "EXP", 290, 46, Color.FromArgb(200, 200, 200), pbExp, lblExpVal, lblExpNum)

        ' Toa do + ban do
        lblCoordVal = New Label() With {.Text = "(?, ?)", .Location = New System.Drawing.Point(690, 18),
                                         .Size = New System.Drawing.Size(140, 18),
                                         .ForeColor = Color.FromArgb(180, 220, 255)}
        grpStat.Controls.Add(lblCoordVal)
        lblMapVal = New Label() With {.Text = "---", .Location = New System.Drawing.Point(690, 46),
                                       .Size = New System.Drawing.Size(196, 18),
                                       .ForeColor = Color.FromArgb(255, 220, 120)}
        grpStat.Controls.Add(lblMapVal)
        Dim lMap As New Label() With {.Text = "Ban do:", .Location = New System.Drawing.Point(600, 46), .Size = New System.Drawing.Size(88, 18)}
        Dim lCoord As New Label() With {.Text = "Toa do:", .Location = New System.Drawing.Point(600, 18), .Size = New System.Drawing.Size(88, 18)}
        grpStat.Controls.AddRange({lCoord, lMap})
        y += 106

        ' ── Auto Combat ─────────────────────────────────────────────
        Dim grpAuto As New GroupBox() With {.Text = "Auto Combat",
                                             .Location = New System.Drawing.Point(8, y),
                                             .Size = New System.Drawing.Size(898, 110),
                                             .ForeColor = Color.FromArgb(140, 130, 100)}
        Me.Controls.Add(grpAuto)

        chkAutoAttack = MakeCheck("Auto-Attack (click mob gan nhat)", 8, 18, 220)
        chkAutoSkill  = MakeCheck("Auto-Skill theo thu tu:", 8, 40, 160)
        chkAutoPickup = MakeCheck("Auto-Pickup (F key khi co item)", 8, 62, 220)
        chkReturnHP   = MakeCheck("Dung danh khi HP <", 8, 84, 148)
        grpAuto.Controls.AddRange({chkAutoAttack, chkAutoSkill, chkAutoPickup, chkReturnHP})

        nudHpThreshold = New NumericUpDown() With {.Location = New System.Drawing.Point(158, 82),
                                                    .Size = New System.Drawing.Size(52, 22),
                                                    .Minimum = 5, .Maximum = 80, .Value = 20,
                                                    .BackColor = Color.FromArgb(28, 30, 38),
                                                    .ForeColor = Color.FromArgb(255, 120, 80)}
        grpAuto.Controls.Add(nudHpThreshold)
        Dim pctLbl As New Label() With {.Text = "%", .Location = New System.Drawing.Point(212, 84), .Size = New System.Drawing.Size(20, 18)}
        grpAuto.Controls.Add(pctLbl)

        ' Skill key checklist
        Dim lblSk As New Label() With {.Text = "Skill keys:", .Location = New System.Drawing.Point(230, 36), .Size = New System.Drawing.Size(68, 18)}
        grpAuto.Controls.Add(lblSk)
        lstSkillKeys = New CheckedListBox() With {.Location = New System.Drawing.Point(300, 16),
                                                   .Size = New System.Drawing.Size(260, 84),
                                                   .BackColor = Color.FromArgb(20, 22, 28),
                                                   .ForeColor = Color.FromArgb(200, 185, 130),
                                                   .Font = New Font("Consolas", 8),
                                                   .CheckOnClick = True}
        For Each sk As String In {"1 - Skill 1", "2 - Skill 2", "3 - Skill 3",
                                   "4 - Skill 4", "5 - Skill 5", "6 - Skill 6",
                                   "F - Nhat do"}
            lstSkillKeys.Items.Add(sk, False)
        Next
        grpAuto.Controls.Add(lstSkillKeys)

        Dim lblDelay As New Label() With {.Text = "Skill delay(ms):", .Location = New System.Drawing.Point(568, 18), .Size = New System.Drawing.Size(110, 18)}
        grpAuto.Controls.Add(lblDelay)
        nudSkillDelay = New NumericUpDown() With {.Location = New System.Drawing.Point(680, 16),
                                                   .Size = New System.Drawing.Size(72, 22),
                                                   .Minimum = 100, .Maximum = 5000, .Value = 800,
                                                   .BackColor = Color.FromArgb(28, 30, 38),
                                                   .ForeColor = Color.FromArgb(220, 200, 140)}
        grpAuto.Controls.Add(nudSkillDelay)
        y += 120

        ' ── Detect ──────────────────────────────────────────────────
        Dim grpDet As New GroupBox() With {.Text = "OpenCV Detect",
                                            .Location = New System.Drawing.Point(8, y),
                                            .Size = New System.Drawing.Size(898, 80),
                                            .ForeColor = Color.FromArgb(140, 130, 100)}
        Me.Controls.Add(grpDet)

        chkDetectMob  = MakeCheck("Mob/Enemy", 8,   18, 90)
        chkDetectItem = MakeCheck("Item drop",  100, 18, 80)
        chkDetectNPC  = MakeCheck("NPC",        182, 18, 60)
        grpDet.Controls.AddRange({chkDetectMob, chkDetectItem, chkDetectNPC})

        Dim lblTpl As New Label() With {.Text = "Templates:", .Location = New System.Drawing.Point(8, 46), .Size = New System.Drawing.Size(72, 18)}
        grpDet.Controls.Add(lblTpl)
        txtTemplateDir = New TextBox() With {.Text = _templateDir,
                                              .Location = New System.Drawing.Point(82, 44),
                                              .Size = New System.Drawing.Size(280, 22),
                                              .BackColor = Color.FromArgb(28, 30, 38),
                                              .ForeColor = Color.FromArgb(200, 185, 130)}
        grpDet.Controls.Add(txtTemplateDir)
        btnBrowseTpl = MakeBtn("...", 366, 43, 36, 24)
        AddHandler btnBrowseTpl.Click, AddressOf OnBrowseTemplates
        grpDet.Controls.Add(btnBrowseTpl)

        Dim lblThr As New Label() With {.Text = "Threshold:", .Location = New System.Drawing.Point(410, 46), .Size = New System.Drawing.Size(72, 18)}
        grpDet.Controls.Add(lblThr)
        txtThreshold = New TextBox() With {.Text = "0.72",
                                            .Location = New System.Drawing.Point(484, 44),
                                            .Size = New System.Drawing.Size(50, 22),
                                            .BackColor = Color.FromArgb(28, 30, 38),
                                            .ForeColor = Color.FromArgb(220, 200, 140),
                                            .TextAlign = HorizontalAlignment.Center}
        grpDet.Controls.Add(txtThreshold)

        lstDetect = New ListBox() With {.Location = New System.Drawing.Point(544, 14),
                                         .Size = New System.Drawing.Size(346, 60),
                                         .BackColor = Color.FromArgb(20, 22, 28),
                                         .ForeColor = Color.FromArgb(180, 210, 150),
                                         .Font = New Font("Consolas", 8)}
        grpDet.Controls.Add(lstDetect)
        y += 90

        ' ── Record / Replay ─────────────────────────────────────────
        Dim grpRec As New GroupBox() With {.Text = "Record / Replay Farming Route",
                                            .Location = New System.Drawing.Point(8, y),
                                            .Size = New System.Drawing.Size(898, 160),
                                            .ForeColor = Color.FromArgb(140, 130, 100)}
        Me.Controls.Add(grpRec)

        Dim ry As Integer = 16
        btnRecord     = MakeBtn("⏺ Record", 8,   ry, 90, 26)
        btnStopRecord = MakeBtn("⏹ Stop",   104, ry, 80, 26)
        btnStopRecord.Enabled = False
        AddHandler btnRecord.Click,     AddressOf OnStartRecord
        AddHandler btnStopRecord.Click, AddressOf OnStopRecord
        grpRec.Controls.AddRange({btnRecord, btnStopRecord})

        chkRecordMouse = MakeCheck("Mouse", 192, ry + 4, 62) : chkRecordMouse.Checked = True
        chkRecordKeys  = MakeCheck("Keys",  258, ry + 4, 50) : chkRecordKeys.Checked  = True
        chkRelative    = MakeCheck("Relative", 312, ry + 4, 72) : chkRelative.Checked = True
        grpRec.Controls.AddRange({chkRecordMouse, chkRecordKeys, chkRelative})

        lblRecordStatus = New Label() With {.Text = "Chua ghi. F12 de dung.",
                                             .Location = New System.Drawing.Point(8, ry + 30),
                                             .Size = New System.Drawing.Size(480, 16),
                                             .ForeColor = Color.FromArgb(120, 120, 100)}
        grpRec.Controls.Add(lblRecordStatus)

        lstActions = New ListBox() With {.Location = New System.Drawing.Point(8, ry + 50),
                                          .Size = New System.Drawing.Size(480, 100),
                                          .BackColor = Color.FromArgb(20, 22, 28),
                                          .ForeColor = Color.FromArgb(170, 190, 160),
                                          .Font = New Font("Consolas", 8),
                                          .ScrollAlwaysVisible = True}
        grpRec.Controls.Add(lstActions)

        ' Replay controls
        Dim lblLoop As New Label() With {.Text = "Loop:", .Location = New System.Drawing.Point(500, ry), .Size = New System.Drawing.Size(40, 18)}
        grpRec.Controls.Add(lblLoop)
        nudLoopCount = New NumericUpDown() With {.Location = New System.Drawing.Point(542, ry - 2),
                                                  .Size = New System.Drawing.Size(58, 22),
                                                  .Minimum = 1, .Maximum = 9999, .Value = 1,
                                                  .BackColor = Color.FromArgb(28, 30, 38),
                                                  .ForeColor = Color.FromArgb(220, 200, 140)}
        grpRec.Controls.Add(nudLoopCount)

        btnReplay     = MakeBtn("▶ Replay", 606, ry, 90, 26)
        btnReplay.BackColor = Color.FromArgb(20, 50, 80)
        btnReplay.ForeColor = Color.FromArgb(80, 180, 255)
        btnStopReplay = MakeBtn("⏹ Stop",   702, ry, 72, 26)
        btnStopReplay.Enabled = False
        AddHandler btnReplay.Click,     AddressOf OnStartReplay
        AddHandler btnStopReplay.Click, AddressOf OnStopReplay
        grpRec.Controls.AddRange({btnReplay, btnStopReplay})

        ' JSON Profile
        Dim lblProf As New Label() With {.Text = "Profile:", .Location = New System.Drawing.Point(500, ry + 36), .Size = New System.Drawing.Size(50, 18)}
        grpRec.Controls.Add(lblProf)
        cmbProfiles = New ComboBox() With {.Location = New System.Drawing.Point(552, ry + 33),
                                            .Size = New System.Drawing.Size(200, 22),
                                            .BackColor = Color.FromArgb(28, 30, 38),
                                            .ForeColor = Color.FromArgb(200, 185, 130),
                                            .DropDownStyle = ComboBoxStyle.DropDownList}
        AddHandler cmbProfiles.SelectedIndexChanged, AddressOf OnProfileSelected
        grpRec.Controls.Add(cmbProfiles)

        txtJsonPath = New TextBox() With {.Text = "vltk_route.json",
                                           .Location = New System.Drawing.Point(552, ry + 59),
                                           .Size = New System.Drawing.Size(200, 22),
                                           .BackColor = Color.FromArgb(28, 30, 38),
                                           .ForeColor = Color.FromArgb(200, 185, 130)}
        grpRec.Controls.Add(txtJsonPath)

        btnSaveJson      = MakeBtn("Save", 756, ry + 32, 60, 24)
        btnLoadJson      = MakeBtn("Load", 756, ry + 58, 60, 24)
        btnDeleteProfile = MakeBtn("Del",  820, ry + 32, 40, 24)
        btnDeleteProfile.ForeColor = Color.FromArgb(255, 80, 80)
        AddHandler btnSaveJson.Click,      AddressOf OnSaveJson
        AddHandler btnLoadJson.Click,      AddressOf OnLoadJson
        AddHandler btnDeleteProfile.Click, AddressOf OnDeleteProfile
        grpRec.Controls.AddRange({btnSaveJson, btnLoadJson, btnDeleteProfile})
        y += 170

        ' ── Preview ─────────────────────────────────────────────────
        Dim grpPrev As New GroupBox() With {.Text = "Preview vung chup",
                                             .Location = New System.Drawing.Point(8, y),
                                             .Size = New System.Drawing.Size(898, 86),
                                             .ForeColor = Color.FromArgb(140, 130, 100)}
        Me.Controls.Add(grpPrev)

        Dim lblPZ As New Label() With {.Text = "Vung:", .Location = New System.Drawing.Point(8, 20), .Size = New System.Drawing.Size(40, 18)}
        grpPrev.Controls.Add(lblPZ)
        cmbPreviewZone = New ComboBox() With {.Location = New System.Drawing.Point(50, 17),
                                               .Size = New System.Drawing.Size(90, 22),
                                               .BackColor = Color.FromArgb(28, 30, 38),
                                               .ForeColor = Color.FromArgb(200, 185, 130),
                                               .DropDownStyle = ComboBoxStyle.DropDownList}
        cmbPreviewZone.Items.AddRange({"hp", "mp", "nl", "exp", "coord", "map"})
        cmbPreviewZone.SelectedIndex = 0
        grpPrev.Controls.Add(cmbPreviewZone)

        picPreview = New PictureBox() With {.Location = New System.Drawing.Point(148, 8),
                                             .Size = New System.Drawing.Size(738, 72),
                                             .BackColor = Color.Black,
                                             .SizeMode = PictureBoxSizeMode.Zoom,
                                             .BorderStyle = BorderStyle.FixedSingle}
        grpPrev.Controls.Add(picPreview)
        y += 96

        ' ── Log ─────────────────────────────────────────────────────
        Dim grpLog As New GroupBox() With {.Text = "Log",
                                            .Location = New System.Drawing.Point(8, y),
                                            .Size = New System.Drawing.Size(898, 72),
                                            .ForeColor = Color.FromArgb(140, 130, 100)}
        Me.Controls.Add(grpLog)
        txtLog = New RichTextBox() With {.Location = New System.Drawing.Point(6, 14),
                                          .Size = New System.Drawing.Size(884, 52),
                                          .BackColor = Color.FromArgb(14, 16, 20),
                                          .ForeColor = Color.FromArgb(160, 200, 140),
                                          .Font = New Font("Consolas", 8),
                                          .ReadOnly = True,
                                          .ScrollBars = RichTextBoxScrollBars.Vertical}
        grpLog.Controls.Add(txtLog)
        Me.ClientSize = New System.Drawing.Size(914, y + 80)
    End Sub

    Private Sub AddStatRow(parent As Control, label As String, x As Integer, y As Integer,
                             barColor As Color, ByRef pb As ProgressBar, ByRef lval As Label, ByRef lnum As Label)
        Dim lbl As New Label() With {.Text = label, .Location = New System.Drawing.Point(x, y + 2), .Size = New System.Drawing.Size(30, 16)}
        lval = New Label() With {.Text = "---", .Location = New System.Drawing.Point(x + 32, y + 2),
                                  .Size = New System.Drawing.Size(40, 16), .ForeColor = barColor}
        lnum = New Label() With {.Text = "", .Location = New System.Drawing.Point(x + 74, y + 2),
                                  .Size = New System.Drawing.Size(90, 16), .ForeColor = Color.FromArgb(170, 170, 170),
                                  .Font = New Font("Consolas", 7.5)}
        pb = New ProgressBar() With {.Location = New System.Drawing.Point(x, y + 20),
                                      .Size = New System.Drawing.Size(270, 12), .Maximum = 100}
        parent.Controls.AddRange({lbl, lval, lnum, pb})
    End Sub

    Private Function MakeBtn(text As String, x As Integer, y As Integer, w As Integer, h As Integer) As Button
        Return New Button() With {
            .Text      = text, .Location = New System.Drawing.Point(x, y), .Size = New System.Drawing.Size(w, h),
            .BackColor = Color.FromArgb(30, 35, 48), .ForeColor = Color.FromArgb(200, 185, 130),
            .Font = New Font("Consolas", 8, FontStyle.Bold), .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand}
    End Function

    Private Function MakeCheck(text As String, x As Integer, y As Integer, w As Integer) As CheckBox
        Return New CheckBox() With {.Text = text, .Location = New System.Drawing.Point(x, y),
                                     .Size = New System.Drawing.Size(w, 20),
                                     .ForeColor = Color.FromArgb(200, 195, 170)}
    End Function

    Private Sub AddLabel(text As String, x As Integer, y As Integer, w As Integer)
        Me.Controls.Add(New Label() With {.Text = text, .Location = New System.Drawing.Point(x, y), .Size = New System.Drawing.Size(w, 20)})
    End Sub

    Private Function AddCombo(x As Integer, y As Integer, w As Integer, items() As String) As ComboBox
        Dim c As New ComboBox() With {.Location = New System.Drawing.Point(x, y), .Size = New System.Drawing.Size(w, 24),
                                       .BackColor = Color.FromArgb(28, 30, 38), .ForeColor = Color.FromArgb(220, 200, 140),
                                       .DropDownStyle = ComboBoxStyle.DropDownList}
        c.Items.AddRange(items)
        Me.Controls.Add(c)
        Return c
    End Function
#End Region

#Region "Process / Window"
    Private Sub RefreshProcessList()
        cmbProcess.Items.Clear()
        For Each p As Process In Process.GetProcesses()
            Try
                If p.MainWindowHandle <> IntPtr.Zero Then cmbProcess.Items.Add(p.ProcessName)
            Catch : End Try
        Next
        ' Auto-select VLTK
        Dim targets() As String = {"elementclient", "vltk", "gameclient", "element"}
        For i As Integer = 0 To cmbProcess.Items.Count - 1
            Dim nm As String = cmbProcess.Items(i).ToString().ToLower()
            For Each t As String In targets
                If nm.Contains(t) Then cmbProcess.SelectedIndex = i : Exit For
            Next
            If cmbProcess.SelectedIndex >= 0 Then Exit For
        Next
        If cmbProcess.SelectedIndex < 0 AndAlso cmbProcess.Items.Count > 0 Then cmbProcess.SelectedIndex = 0
    End Sub

    Private Sub OnRefreshProcess(s As Object, e As EventArgs)
        RefreshProcessList() : Log("Da lam moi process list.")
    End Sub

    Private Sub OnProcessSelected(s As Object, e As EventArgs)
        DetectAndApplyWindowSize()
    End Sub

    Private Sub OnRedetect(s As Object, e As EventArgs)
        DetectAndApplyWindowSize()
    End Sub

    Private Sub OnInputModeChanged(s As Object, e As EventArgs)
        _inputMode = CType(cmbInputMode.SelectedIndex, InputMode)
        Log("[Input] " & _inputMode.ToString())
    End Sub

    Private Sub DetectAndApplyWindowSize()
        If cmbProcess.SelectedItem Is Nothing Then Return
        Dim procs() As Process = Process.GetProcessesByName(cmbProcess.SelectedItem.ToString())
        If procs.Length = 0 Then Return
        Dim hWnd As IntPtr = procs(0).MainWindowHandle
        If hWnd = IntPtr.Zero Then Return
        Dim clientRc As New RECT()
        GetClientRect(hWnd, clientRc)
        Dim winW As Integer = clientRc.Right
        Dim winH As Integer = clientRc.Bottom
        If winW < 200 OrElse winH < 200 Then Return
        _detectedWinW = winW : _detectedWinH = winH
        lblWinInfo.Text      = String.Format("Client: {0}x{1}  [{2}]", winW, winH, cmbProcess.SelectedItem)
        lblWinInfo.ForeColor = Color.FromArgb(60, 220, 100)
        Dim sx As Double = winW / CDbl(BASE_W)
        Dim sy As Double = winH / CDbl(BASE_H)
        Scale4(txtHpX,    txtHpY,    txtHpW,    txtHpH,    DEF_HP_X,    DEF_HP_Y,    DEF_HP_W,    DEF_HP_H,    sx, sy)
        Scale4(txtMpX,    txtMpY,    txtMpW,    txtMpH,    DEF_MP_X,    DEF_MP_Y,    DEF_MP_W,    DEF_MP_H,    sx, sy)
        Scale4(txtExpX,   txtExpY,   txtExpW,   txtExpH,   DEF_EXP_X,   DEF_EXP_Y,   DEF_EXP_W,   DEF_EXP_H,   sx, sy)
        Scale4(txtCoordX, txtCoordY, txtCoordW, txtCoordH, DEF_COORD_X, DEF_COORD_Y, DEF_COORD_W, DEF_COORD_H, sx, sy)
        Scale4(txtMapX,   txtMapY,   txtMapW,   txtMapH,   DEF_MAP_X,   DEF_MAP_Y,   DEF_MAP_W,   DEF_MAP_H,   sx, sy)
        Log(String.Format("[Auto] VLTK client {0}x{1} -> scale {2:F3}x{3:F3} tu 800x600", winW, winH, sx, sy))
    End Sub

    Private Shared Sub Scale4(txX As TextBox, txY As TextBox, txW As TextBox, txH As TextBox,
                                dx As String, dy As String, dw As String, dh As String,
                                sx As Double, sy As Double)
        Dim bx, by, bw, bh As Integer
        Integer.TryParse(dx, bx) : Integer.TryParse(dy, by)
        Integer.TryParse(dw, bw) : Integer.TryParse(dh, bh)
        txX.Text = CInt(Math.Round(bx * sx)).ToString() : txY.Text = CInt(Math.Round(by * sy)).ToString()
        txW.Text = CInt(Math.Round(bw * sx)).ToString() : txH.Text = CInt(Math.Round(bh * sy)).ToString()
    End Sub

    Private Function GetGameRect() As RECT
        Dim rc As New RECT()
        If cmbProcess.SelectedItem Is Nothing Then Return rc
        Dim procs() As Process = Process.GetProcessesByName(cmbProcess.SelectedItem.ToString())
        If procs.Length = 0 Then Return rc
        Dim hWnd As IntPtr = procs(0).MainWindowHandle
        If hWnd = IntPtr.Zero Then Return rc
        Dim clientRc As New RECT() : Dim origin As New POINT()
        GetClientRect(hWnd, clientRc) : ClientToScreen(hWnd, origin)
        rc.Left = origin.X : rc.Top = origin.Y
        rc.Right = origin.X + clientRc.Right : rc.Bottom = origin.Y + clientRc.Bottom
        Return rc
    End Function

    Private Function GetGameHwnd() As IntPtr
        If cmbProcess.SelectedItem Is Nothing Then Return IntPtr.Zero
        Dim procs() As Process = Process.GetProcessesByName(cmbProcess.SelectedItem.ToString())
        Return If(procs.Length > 0, procs(0).MainWindowHandle, IntPtr.Zero)
    End Function
#End Region

#Region "OCR Init"
    Private Sub InitOcr()
        Try
            Dim tessDir As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata")
            If Not Directory.Exists(tessDir) Then
                Log("[OCR] Khong tim thay tessdata\. Toa do/Map se khong doc duoc.")
                Return
            End If
            _ocrEngine = New TesseractEngine(tessDir, "vie+eng", EngineMode.Default)
            _ocrEngine.SetVariable("tessedit_char_whitelist", "0123456789,()ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz ")
            Log("[OCR] Tesseract san sang (vie+eng).")
        Catch ex As Exception
            Log("[OCR] Loi: " & ex.Message)
        End Try
    End Sub
#End Region

#Region "Toggle Active"
    Private Sub OnToggleActive(s As Object, e As EventArgs)
        If _running Then
            _running = False
            tmrStat.Stop() : tmrDetect.Stop() : tmrAuto.Stop()
            btnActive.Text = "▶  ACTIVE" : btnActive.BackColor = Color.FromArgb(20, 60, 20) : btnActive.ForeColor = Color.FromArgb(80, 220, 80)
            Log("[||] Da dung.")
        Else
            If cmbProcess.SelectedItem Is Nothing Then Log("[!!] Chua chon process!") : Return
            Double.TryParse(txtThreshold.Text.Replace(",", "."),
                            Globalization.NumberStyles.Any,
                            Globalization.CultureInfo.InvariantCulture, _threshold)
            _templateDir = txtTemplateDir.Text.Trim()
            _running = True
            tmrStat.Start()
            If chkDetectMob.Checked OrElse chkDetectItem.Checked OrElse chkDetectNPC.Checked Then tmrDetect.Start()
            If chkAutoAttack.Checked OrElse chkAutoSkill.Checked OrElse chkAutoPickup.Checked Then tmrAuto.Start()
            btnActive.Text = "⏹  STOP" : btnActive.BackColor = Color.FromArgb(70, 20, 20) : btnActive.ForeColor = Color.FromArgb(255, 80, 80)
            Log("[>>] Bat dau: " & cmbProcess.SelectedItem.ToString())
        End If
    End Sub
#End Region

#Region "Stat Reading"
    Private Sub OnStatTick(s As Object, e As EventArgs)
        If Not _running Then Return
        Dim rc As RECT = GetGameRect()

        ' HP / MP / NL / EXP qua LockBits color scan
        Dim hp As Integer = ReadBarPct(rc, txtHpX, txtHpY, txtHpW, txtHpH, "hp")
        Dim mp As Integer = ReadBarPct(rc, txtMpX, txtMpY, txtMpW, txtMpH, "mp")
        Dim nl As Integer = ReadBarPct(rc, txtNlX, txtNlY, txtNlW, txtNlH, "nl")
        Dim ep As Integer = ReadBarPct(rc, txtExpX, txtExpY, txtExpW, txtExpH, "exp")
        _lastHpPct = hp : _lastMpPct = mp
        lblHpVal.Text = hp & "%" : lblMpVal.Text = mp & "%" : lblNlVal.Text = nl & "%" : lblExpVal.Text = ep & "%"
        pbHp.Value = Math.Max(0, Math.Min(100, hp))
        pbMp.Value = Math.Max(0, Math.Min(100, mp))
        pbNl.Value = Math.Max(0, Math.Min(100, nl))
        pbExp.Value = Math.Max(0, Math.Min(100, ep))

        ' Toa do + Ban do qua OCR
        If _ocrEngine IsNot Nothing Then
            lblCoordVal.Text = ReadOcrText(rc, txtCoordX, txtCoordY, txtCoordW, txtCoordH, True)
            lblMapVal.Text   = ReadOcrText(rc, txtMapX,   txtMapY,   txtMapW,   txtMapH,   False)

            ' Doc so thuc (vd "180/180") de len tren thanh HP/MP/NL/EXP qua OCR.
            ' Chi doc moi vong tick thu 3 de do tai CPU, vi OCR nang hon nhieu so voi scan mau.
            _ocrStatCounter = (_ocrStatCounter + 1) Mod 3
            If _ocrStatCounter = 0 Then
                lblHpNum.Text  = ReadOcrText(rc, txtHpX,  txtHpY,  txtHpW,  txtHpH,  True)
                lblMpNum.Text  = ReadOcrText(rc, txtMpX,  txtMpY,  txtMpW,  txtMpH,  True)
                lblNlNum.Text  = ReadOcrText(rc, txtNlX,  txtNlY,  txtNlW,  txtNlH,  True)
                lblExpNum.Text = ReadOcrText(rc, txtExpX, txtExpY, txtExpW, txtExpH, True)
            End If
        End If

        ' Preview
        UpdatePreview(rc)
    End Sub

    ''' <summary>
    ''' Doc % thanh HP/MP/NL/EXP bang LockBits - nhanh ~50x GetPixel.
    ''' VLTK1 mau thanh (da do pixel that tu anh chup gameplay):
    '''   HP  : xanh la  (G>130, R<170, B<110)
    '''   MP  : do       (R>150, G<100, B<100)
    '''   NL  : xanh duong (B>140, R<110, G<140)
    '''   EXP : bac/xam sang tren nen toi (khong co mau dac trung - dung do sang)
    ''' </summary>
    Private Function ReadBarPct(rc As RECT,
                                 txX As TextBox, txY As TextBox,
                                 txW As TextBox, txH As TextBox,
                                 tag As String) As Integer
        Try
            Dim absX As Integer = rc.Left + ParseInt(txX.Text)
            Dim absY As Integer = rc.Top  + ParseInt(txY.Text)
            Dim w    As Integer = ParseInt(txW.Text)
            Dim h    As Integer = ParseInt(txH.Text)
            If w < 2 OrElse h < 2 Then Return 0
            Using bmp As Bitmap = CaptureRegion(absX, absY, w, h)
                Dim midY   As Integer  = Math.Max(0, bmp.Height \ 2)
                Dim rect   As New Rectangle(0, midY, bmp.Width, 1)
                Dim bd     As BitmapData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
                Dim rowLen As Integer  = bmp.Width * 4
                Dim row(rowLen - 1) As Byte
                Marshal.Copy(bd.Scan0, row, 0, rowLen)
                bmp.UnlockBits(bd)
                ' Mau "day" (filled) lay tai pixel trai cung, mau "nen" (rong) lay tai pixel phai cung.
                ' Tu dong thich ung voi BAT KY mau thanh nao (xanh/do/vang/cam...) thay vi doan mau
                ' co dinh, vi moi client/skin VLTK co the dung mau khac nhau cho HP/MP/NL.
                Dim fgB As Integer = row(0) : Dim fgG As Integer = row(1) : Dim fgR As Integer = row(2)
                Dim bgB As Integer = row(rowLen - 4) : Dim bgG As Integer = row(rowLen - 3) : Dim bgR As Integer = row(rowLen - 2)
                Dim fgBrightness As Integer = fgR + fgG + fgB
                Dim fgBgDist      As Integer = Math.Abs(fgR - bgR) + Math.Abs(fgG - bgG) + Math.Abs(fgB - bgB)

                ' Neu 2 dau giong het mau nhau -> thanh dang 100% day hoac 0% rong het
                If fgBgDist < 30 Then
                    Return If(fgBrightness > 90, 100, 0)
                End If

                Dim lit As Integer = 0
                For px As Integer = 0 To bmp.Width - 1
                    Dim i As Integer = px * 4  ' BGRA order
                    Dim b As Integer = row(i) : Dim g As Integer = row(i + 1) : Dim r As Integer = row(i + 2)
                    Dim distFg As Integer = Math.Abs(r - fgR) + Math.Abs(g - fgG) + Math.Abs(b - fgB)
                    Dim distBg As Integer = Math.Abs(r - bgR) + Math.Abs(g - bgG) + Math.Abs(b - bgB)
                    If distFg <= distBg Then lit += 1
                Next
                Return CInt(lit * 100 / bmp.Width)
            End Using
        Catch ex As Exception
            Log("[Bar] Loi " & tag & ": " & ex.Message)
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Doc text qua Tesseract OCR (toa do XY, ten ban do).
    ''' VLTK1 hien toa do dang "(X, Y)" tren minimap.
    ''' </summary>
    Private Function ReadOcrText(rc As RECT,
                                  txX As TextBox, txY As TextBox,
                                  txW As TextBox, txH As TextBox,
                                  numbersOnly As Boolean) As String
        Try
            Dim absX As Integer = rc.Left + ParseInt(txX.Text)
            Dim absY As Integer = rc.Top  + ParseInt(txY.Text)
            Dim w    As Integer = ParseInt(txW.Text)
            Dim h    As Integer = ParseInt(txH.Text)
            If w < 4 OrElse h < 4 Then Return "---"
            Using bmp As Bitmap = CaptureRegion(absX, absY, w, h)
            Using proc As Bitmap = PreprocessOcr(bmp, numbersOnly)
            Using pix As Pix = BitmapToPix(proc)
            Using page As Page = _ocrEngine.Process(pix, PageSegMode.SingleLine)
                Dim txt As String = page.GetText().Trim()
                If numbersOnly Then
                    Dim clean As New StringBuilder()
                    For Each c As Char In txt
                        If Char.IsDigit(c) OrElse c = "," OrElse c = "(" OrElse c = ")" OrElse c = " " OrElse c = "/" Then
                            clean.Append(c)
                        End If
                    Next
                    Return If(clean.Length > 0, clean.ToString().Trim(), "---")
                End If
                Return If(txt.Length > 0, txt, "---")
            End Using : End Using : End Using : End Using
        Catch
            Return "---"
        End Try
    End Function

    ''' <summary>
    ''' Preprocess bitmap truoc OCR:
    '''   - Scale 3x (Bicubic)
    '''   - Grayscale nhanh qua LockBits
    '''   - Threshold: chon mau sang (VLTK text mau vang/trang tren nen toi)
    ''' </summary>
    Private Shared Function BitmapToPix(bmp As Bitmap) As Pix
        Using ms As New MemoryStream()
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png)
            Return Pix.LoadFromMemory(ms.ToArray())
        End Using
    End Function

    Private Shared Function PreprocessOcr(src As Bitmap, highContrast As Boolean) As Bitmap
        Dim sw As Integer = src.Width * 3
        Dim sh As Integer = src.Height * 3
        Dim scaled As New Bitmap(sw, sh)
        Using g As Graphics = Graphics.FromImage(scaled)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.DrawImage(src, 0, 0, sw, sh)
        End Using
        Dim result As New Bitmap(sw, sh, PixelFormat.Format32bppArgb)
        Dim bdS As BitmapData = scaled.LockBits(New Rectangle(0, 0, sw, sh), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Dim bdD As BitmapData = result.LockBits(New Rectangle(0, 0, sw, sh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Dim len As Integer = bdS.Stride * sh
        Dim buf(len - 1) As Byte : Dim out(len - 1) As Byte
        Marshal.Copy(bdS.Scan0, buf, 0, len)
        Dim thr As Integer = If(highContrast, 80, 60)
        For i As Integer = 0 To len - 1 Step 4
            Dim gray As Integer = CInt(buf(i) * 0.11 + buf(i+1) * 0.59 + buf(i+2) * 0.30)
            Dim v As Byte = If(gray > thr, CByte(255), CByte(0))
            out(i) = v : out(i+1) = v : out(i+2) = v : out(i+3) = 255
        Next
        Marshal.Copy(out, 0, bdD.Scan0, len)
        scaled.UnlockBits(bdS) : result.UnlockBits(bdD)
        scaled.Dispose()
        Return result
    End Function
#End Region

#Region "OpenCV Detect - Mob / Item / NPC"
    Private Sub OnDetectTick(s As Object, e As EventArgs)
        If Not _running Then Return
        If Interlocked.CompareExchange(_detectBusy, 1, 0) <> 0 Then Return

        Dim rc          As RECT    = GetGameRect()
        Dim doMob       As Boolean = chkDetectMob.Checked
        Dim doItem      As Boolean = chkDetectItem.Checked
        Dim doNPC       As Boolean = chkDetectNPC.Checked
        Dim tplDir      As String  = _templateDir
        Dim thr         As Double  = _threshold
        Dim winW        As Integer = rc.Right  - rc.Left
        Dim winH        As Integer = rc.Bottom - rc.Top
        If winW < 200 OrElse winH < 200 Then _detectBusy = 0 : Return

        ' Capture vung choi (bot tru HUD)
        Dim capTop As Integer = DETECT_TOP
        Dim capH   As Integer = winH - DETECT_BOTTOM - DETECT_TOP
        If capH < 50 Then _detectBusy = 0 : Return
        Dim bmpSrc As Bitmap = CaptureRegion(rc.Left, rc.Top + capTop, winW, capH)

        Task.Run(Async Function()
            Try
                Dim results As New List(Of DetectResult)()

                ' 1. Scan mau name tag mob (chu do tren dau nhan vat)
                If doMob Then results.AddRange(DetectMobByNameColor(bmpSrc))

                ' 2. Scan item drop (diem sang vang/trang giua man hinh)
                If doItem Then results.AddRange(DetectItemByGlow(bmpSrc))

                ' 3. Template matching mob/NPC
                For Each pair In {(doMob, "mobs"), (doNPC, "npcs")}
                    If pair.Item1 AndAlso Directory.Exists(tplDir) Then
                        Dim sub2 As String = Path.Combine(tplDir, pair.Item2)
                        If Directory.Exists(sub2) Then
                            For Each f As String In Directory.GetFiles(sub2, "*.png")
                                Dim isMob As Boolean = (pair.Item2 = "mobs")
                                Dim r As DetectResult = TemplateMatch(bmpSrc, f, isMob, False, thr)
                                If r IsNot Nothing Then results.Add(r)
                            Next
                        End If
                    End If
                Next

                ' 4. Template matching item drop
                If doItem AndAlso Directory.Exists(tplDir) Then
                    Dim itemDir As String = Path.Combine(tplDir, "items")
                    If Directory.Exists(itemDir) Then
                        For Each f As String In Directory.GetFiles(itemDir, "*.png")
                            Dim r As DetectResult = TemplateMatch(bmpSrc, f, False, True, thr)
                            If r IsNot Nothing Then results.Add(r)
                        Next
                    End If
                End If

                _lastDetected = results

                Await Task.Factory.StartNew(
                    Sub() UpdateDetectUI(results),
                    CancellationToken.None, TaskCreationOptions.None,
                    TaskScheduler.FromCurrentSynchronizationContext())
            Catch ex As Exception
                Log("[Detect] Loi: " & ex.Message)
            Finally
                bmpSrc.Dispose()
                _detectBusy = 0
            End Try
        End Function)
    End Sub

    ''' <summary>
    ''' Detect mob bang mau chu ten tren dau (VLTK: do R>180 G<80 B<80).
    ''' Scan cac hang trong phan giua-tren cua vung choi.
    ''' </summary>
    Private Function DetectMobByNameColor(bmp As Bitmap) As List(Of DetectResult)
        Dim result As New List(Of DetectResult)()
        Try
            Dim bd As BitmapData = bmp.LockBits(
                New Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Dim stride As Integer = bd.Stride
            Dim buf(stride * bmp.Height - 1) As Byte
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length)
            bmp.UnlockBits(bd)

            Dim maxRow As Integer = bmp.Height * 2 \ 3
            Dim stepRow As Integer = Math.Max(1, bmp.Height \ 30)
            Dim rowY As Integer = 10
            Do While rowY < maxRow
                Dim rowOff   As Integer = rowY * stride
                Dim runStart As Integer = -1
                For px As Integer = 0 To bmp.Width - 1
                    Dim i As Integer = rowOff + px * 4
                    Dim b As Byte = buf(i) : Dim g As Byte = buf(i+1) : Dim r As Byte = buf(i+2)
                    ' Chu ten mob mau do dam
                    Dim isMobName As Boolean = (r > 180 AndAlso g < 80 AndAlso b < 80)
                    If isMobName Then
                        If runStart < 0 Then runStart = px
                    Else
                        If runStart >= 0 Then
                            Dim runLen As Integer = px - runStart
                            ' Ten mob dai 20-120px (tuong ung ~3-20 ky tu)
                            If runLen >= 20 AndAlso runLen <= 120 Then
                                result.Add(New DetectResult() With {
                                    .Label  = "Mob",
                                    .X      = runStart,
                                    .Y      = Math.Max(0, rowY - 4),
                                    .W      = runLen,
                                    .H      = 24,
                                    .Score  = 0.85,
                                    .IsMob  = True,
                                    .IsItem = False
                                })
                            End If
                            runStart = -1
                        End If
                    End If
                Next
                rowY += stepRow
            Loop
        Catch : End Try
        Return result
    End Function

    ''' <summary>
    ''' Detect item drop bang pixel sang (vang/vang kim VLTK: R>200 G>180 B<80).
    ''' Scan nhieu diem ngau nhien trong vung choi, gom cac diem gan nhau.
    ''' </summary>
    Private Function DetectItemByGlow(bmp As Bitmap) As List(Of DetectResult)
        Dim result As New List(Of DetectResult)()
        Try
            Dim bd As BitmapData = bmp.LockBits(
                New Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Dim stride As Integer = bd.Stride
            Dim buf(stride * bmp.Height - 1) As Byte
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length)
            bmp.UnlockBits(bd)

            Dim hotSpots As New List(Of System.Drawing.Point)()
            Dim stepX As Integer = 4 : Dim stepY As Integer = 4
            For rowY As Integer = 10 To bmp.Height - 10 Step stepY
                Dim rowOff As Integer = rowY * stride
                For px As Integer = 10 To bmp.Width - 10 Step stepX
                    Dim i As Integer = rowOff + px * 4
                    Dim b As Byte = buf(i) : Dim g As Byte = buf(i+1) : Dim r As Byte = buf(i+2)
                    ' Item glow: vang sang (cung co the la trang cho item thuong)
                    Dim isGold  As Boolean = (r > 200 AndAlso g > 180 AndAlso b < 80)
                    Dim isWhite As Boolean = (r > 220 AndAlso g > 220 AndAlso b > 200)
                    If isGold OrElse isWhite Then
                        hotSpots.Add(New System.Drawing.Point(px, rowY))
                    End If
                Next
            Next

            ' Gom cac diem gan nhau thanh cluster (khoang cach < 30px)
            Dim used(hotSpots.Count - 1) As Boolean
            For i As Integer = 0 To hotSpots.Count - 1
                If used(i) Then Continue For
                Dim cluster As New List(Of System.Drawing.Point)()
                cluster.Add(hotSpots(i))
                used(i) = True
                For j As Integer = i + 1 To hotSpots.Count - 1
                    If used(j) Then Continue For
                    Dim dx As Integer = hotSpots(j).X - hotSpots(i).X
                    Dim dy As Integer = hotSpots(j).Y - hotSpots(i).Y
                    If Math.Abs(dx) < 30 AndAlso Math.Abs(dy) < 30 Then
                        cluster.Add(hotSpots(j)) : used(j) = True
                    End If
                Next
                If cluster.Count >= 3 Then  ' can it nhat 3 pixel sang
                    Dim cx As Integer = CInt(cluster.Average(Function(p) p.X))
                    Dim cy As Integer = CInt(cluster.Average(Function(p) p.Y))
                    result.Add(New DetectResult() With {
                        .Label  = "Item",
                        .X      = cx - 16, .Y = cy - 16,
                        .W      = 32,      .H = 32,
                        .Score  = 0.8,
                        .IsMob  = False,
                        .IsItem = True
                    })
                End If
            Next
        Catch : End Try
        Return result
    End Function

    Private Shared Function TemplateMatch(src As Bitmap, tplPath As String,
                                           isMob As Boolean, isItem As Boolean,
                                           threshold As Double) As DetectResult
        Try
            Using srcMat As Mat = BitmapToMat(src)
            Using tplMat As Mat = Cv2.ImRead(tplPath, ImreadModes.Color)
                If tplMat.Empty() Then Return Nothing
                If tplMat.Width > srcMat.Width OrElse tplMat.Height > srcMat.Height Then Return Nothing
                Using res As New Mat()
                    Cv2.MatchTemplate(srcMat, tplMat, res, TemplateMatchModes.CCoeffNormed)
                    Dim minV, maxV As Double
                    Dim minL, maxL As OpenCvSharp.Point
                    Cv2.MinMaxLoc(res, minV, maxV, minL, maxL)
                    If maxV >= threshold Then
                        Return New DetectResult() With {
                            .Label  = Path.GetFileNameWithoutExtension(tplPath),
                            .X = maxL.X, .Y = maxL.Y,
                            .W = tplMat.Width, .H = tplMat.Height,
                            .Score = maxV, .IsMob = isMob, .IsItem = isItem}
                    End If
                End Using
            End Using : End Using
        Catch : End Try
        Return Nothing
    End Function

    Private Sub UpdateDetectUI(results As List(Of DetectResult))
        If lstDetect.InvokeRequired Then lstDetect.Invoke(Sub() UpdateDetectUI(results)) : Return
        lstDetect.Items.Clear()
        For Each r As DetectResult In results
            lstDetect.Items.Add(String.Format("[{0}] ({1},{2})  s={3:F2}", r.Label, r.X, r.Y, r.Score))
        Next
    End Sub
#End Region

#Region "Auto Combat / Pickup"
    ''' <summary>
    ''' OnAutoTick: chay moi 200ms, xu ly auto-attack, auto-skill, auto-pickup.
    ''' Rieng biet voi OnDetectTick (400ms) de attack nhanh hon.
    ''' </summary>
    Private Sub OnAutoTick(s As Object, e As EventArgs)
        If Not _running Then Return
        If Interlocked.CompareExchange(_autoBusy, 1, 0) <> 0 Then Return
        Task.Run(Sub()
            Try
                ' Kiem tra HP threshold - neu thap qua thi dung danh
                If chkReturnHP.Checked AndAlso _lastHpPct < CInt(nudHpThreshold.Value) Then
                    Log(String.Format("[Auto] HP {0}% < {1}% - dung danh!", _lastHpPct, CInt(nudHpThreshold.Value)))
                    Return
                End If

                Dim rc As RECT = GetGameRect()
                Dim winW As Integer = rc.Right  - rc.Left
                Dim winH As Integer = rc.Bottom - rc.Top
                Dim detected As List(Of DetectResult) = _lastDetected

                ' Auto-pickup item
                If chkAutoPickup.Checked Then
                    Dim items As List(Of DetectResult) = detected.Where(Function(d) d.IsItem).ToList()
                    For Each item As DetectResult In items
                        Dim absX As Integer = rc.Left + item.X + item.W \ 2
                        Dim absY As Integer = rc.Top  + DETECT_TOP + item.Y + item.H \ 2
                        DoClick(absX, absY, True)  ' click item de nhat
                        Thread.Sleep(120)
                    Next
                End If

                ' Auto-attack: click mob gan nhat
                If chkAutoAttack.Checked Then
                    Dim mobs As List(Of DetectResult) = detected.Where(Function(d) d.IsMob).ToList()
                    If mobs.Count > 0 Then
                        Dim nearest As DetectResult = mobs.OrderBy(
                            Function(d) Math.Sqrt((d.X + d.W\2 - winW\2)^2 + (d.Y + d.H\2 - winH\2)^2)
                        ).First()
                        Dim absX As Integer = rc.Left + nearest.X + nearest.W \ 2
                        Dim absY As Integer = rc.Top  + DETECT_TOP + nearest.Y + nearest.H \ 2
                        DoClick(absX, absY, True)
                    End If
                End If

                ' Auto-skill theo thu tu (vong tron qua danh sach duoc check)
                If chkAutoSkill.Checked Then
                    Dim now As Long = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    Dim delay As Long = CLng(nudSkillDelay.Value)
                    If now - _lastSkillMs >= delay Then
                        Dim checkedKeys As New List(Of String)()
                        Me.BeginInvoke(Sub()
                            For i As Integer = 0 To lstSkillKeys.Items.Count - 1
                                If lstSkillKeys.GetItemChecked(i) Then
                                    checkedKeys.Add(lstSkillKeys.Items(i).ToString().Substring(0, 1))
                                End If
                            Next
                        End Sub)
                        Thread.Sleep(10)  ' cho BeginInvoke
                        If checkedKeys.Count > 0 Then
                            Dim sk As String = checkedKeys(_skillIdx Mod checkedKeys.Count)
                            _skillIdx = (_skillIdx + 1) Mod checkedKeys.Count
                            If sk = "F" Then
                                DoKey(Keys.F)
                            Else
                                Dim digit As Integer = 0
                                If Integer.TryParse(sk, digit) Then
                                    DoKey(CType(Keys.D0 + digit, Keys))
                                End If
                            End If
                            _lastSkillMs = now
                        End If
                    End If
                End If
            Catch ex As Exception
                Log("[Auto] Loi: " & ex.Message)
            Finally
                _autoBusy = 0
            End Try
        End Sub)
    End Sub
#End Region

#Region "Input Injection"
    Private Sub DoClick(absX As Integer, absY As Integer, leftBtn As Boolean)
        Dim hWnd As IntPtr = GetGameHwnd()
        Select Case _inputMode
            Case InputMode.RealMouse
                SetCursorPos(absX, absY)
                Dim inp(1) As INPUT
                inp(0).type = INPUT_MOUSE : inp(0).mi.dwFlags = If(leftBtn, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_RIGHTDOWN)
                inp(1).type = INPUT_MOUSE : inp(1).mi.dwFlags = If(leftBtn, MOUSEEVENTF_LEFTUP,   MOUSEEVENTF_RIGHTUP)
                SendInput(2, inp, Marshal.SizeOf(GetType(INPUT)))
            Case InputMode.PostMsg
                If hWnd = IntPtr.Zero Then Return
                Dim grc As RECT = GetGameRect()
                Dim lp As IntPtr = New IntPtr(((absY - grc.Top) << 16) Or ((absX - grc.Left) And &HFFFF))
                PostMessage(hWnd, If(leftBtn, WM_LBUTTONDOWN, WM_RBUTTONDOWN), New IntPtr(1), lp)
                PostMessage(hWnd, If(leftBtn, WM_LBUTTONUP,   WM_RBUTTONUP),   New IntPtr(0), lp)
            Case InputMode.SendMsg
                If hWnd = IntPtr.Zero Then Return
                Dim grc As RECT = GetGameRect()
                Dim lp As IntPtr = New IntPtr(((absY - grc.Top) << 16) Or ((absX - grc.Left) And &HFFFF))
                SendMessage(hWnd, If(leftBtn, WM_LBUTTONDOWN, WM_RBUTTONDOWN), New IntPtr(1), lp)
                SendMessage(hWnd, If(leftBtn, WM_LBUTTONUP,   WM_RBUTTONUP),   New IntPtr(0), lp)
        End Select
    End Sub

    Private Sub DoKey(key As Keys)
        Dim hWnd As IntPtr = GetGameHwnd()
        Select Case _inputMode
            Case InputMode.RealMouse
                Dim ki(1) As INPUT
                ki(0).type = INPUT_KEYBOARD : ki(0).ki.wVk = CUShort(key)
                ki(1).type = INPUT_KEYBOARD : ki(1).ki.wVk = CUShort(key) : ki(1).ki.dwFlags = KEYEVENTF_KEYUP
                SendInput(2, ki, Marshal.SizeOf(GetType(INPUT)))
            Case InputMode.PostMsg
                If hWnd = IntPtr.Zero Then Return
                PostMessage(hWnd, WM_KEYDOWN, New IntPtr(CInt(key)), IntPtr.Zero)
                PostMessage(hWnd, WM_KEYUP,   New IntPtr(CInt(key)), IntPtr.Zero)
            Case InputMode.SendMsg
                If hWnd = IntPtr.Zero Then Return
                SendMessage(hWnd, WM_KEYDOWN, New IntPtr(CInt(key)), IntPtr.Zero)
                SendMessage(hWnd, WM_KEYUP,   New IntPtr(CInt(key)), IntPtr.Zero)
        End Select
    End Sub
#End Region

#Region "Record"
    Private Sub OnStartRecord(s As Object, e As EventArgs)
        If _recording Then Return
        _currentSession             = New ReplayFile()
        _currentSession.GameProcess = If(cmbProcess.SelectedItem IsNot Nothing, cmbProcess.SelectedItem.ToString(), "")
        _currentSession.MapName     = lblMapVal.Text.Trim()
        _currentSession.Resolution  = String.Format("{0}x{1}", _detectedWinW, _detectedWinH)
        _recordStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        _lastActionMs  = _recordStartMs
        _recording = True
        If chkRecordMouse.Checked OrElse chkRecordKeys.Checked Then InstallHooks(chkRecordMouse.Checked, chkRecordKeys.Checked)
        btnRecord.Enabled = False : btnStopRecord.Enabled = True
        lblRecordStatus.Text      = "Dang ghi... (F12 de dung)"
        lblRecordStatus.ForeColor = Color.FromArgb(255, 60, 60)
        Log("[REC] Bat dau ghi.")
    End Sub

    Private Sub OnStopRecord(s As Object, e As EventArgs)
        If Not _recording Then Return
        _recording = False : RemoveHooks()
        btnRecord.Enabled = True : btnStopRecord.Enabled = False
        lblRecordStatus.Text      = String.Format("Da ghi {0} actions.", _currentSession.Actions.Count)
        lblRecordStatus.ForeColor = Color.FromArgb(80, 200, 120)
        UpdateActionList()
        Log("[REC] Dung. Tong: " & _currentSession.Actions.Count & " actions.")
    End Sub

    Private Sub AddAction(a As RecordedAction)
        Dim now As Long = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        a.Timestamp = now - _recordStartMs : a.DelayMs = now - _lastActionMs
        _lastActionMs = now
        _currentSession.Actions.Add(a)
        If lstActions.InvokeRequired Then
            lstActions.Invoke(Sub() UpdateActionList())
        Else
            UpdateActionList()
        End If
    End Sub

    Private Sub UpdateActionList()
        lstActions.Items.Clear()
        For Each a As RecordedAction In _currentSession.Actions
            lstActions.Items.Add(String.Format("[{0}ms] {1}  ({2},{3})  +{4}ms", a.Timestamp, a.Type, a.X, a.Y, a.DelayMs))
        Next
        If lstActions.Items.Count > 0 Then lstActions.SelectedIndex = lstActions.Items.Count - 1
    End Sub

    Private Function ScreenToGame(x As Integer, y As Integer) As System.Drawing.Point
        If Not chkRelative.Checked Then Return New System.Drawing.Point(x, y)
        Dim rc As RECT = GetGameRect()
        Return New System.Drawing.Point(x - rc.Left, y - rc.Top)
    End Function
#End Region

#Region "Hooks"
    Private Sub InstallHooks(mouse As Boolean, keys As Boolean)
        Using curProc As Process = Process.GetCurrentProcess()
            Using curMod As ProcessModule = curProc.MainModule
                Dim hMod As IntPtr = GetModuleHandle(curMod.ModuleName)
                If mouse Then _mouseProc   = AddressOf MouseHookCB   : _mouseHook    = SetWindowsHookEx(WH_MOUSE_LL,    _mouseProc, hMod, 0)
                If keys  Then _keyProc     = AddressOf KeyHookCB     : _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc,   hMod, 0)
            End Using
        End Using
    End Sub

    Private Sub RemoveHooks()
        If _mouseHook    <> IntPtr.Zero Then UnhookWindowsHookEx(_mouseHook)    : _mouseHook    = IntPtr.Zero
        If _keyboardHook <> IntPtr.Zero Then UnhookWindowsHookEx(_keyboardHook) : _keyboardHook = IntPtr.Zero
    End Sub

    Private Function MouseHookCB(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        If nCode >= 0 AndAlso _recording Then
            Dim ms  As MSLLHOOKSTRUCT = Marshal.PtrToStructure(Of MSLLHOOKSTRUCT)(lParam)
            Dim gpt As System.Drawing.Point = ScreenToGame(ms.pt.X, ms.pt.Y)
            Select Case wParam.ToInt32()
                Case &H200  ' WM_MOUSEMOVE
                    If Math.Abs(gpt.X - _lastMousePt.X) > MOUSE_MOVE_THRESHOLD OrElse
                       Math.Abs(gpt.Y - _lastMousePt.Y) > MOUSE_MOVE_THRESHOLD Then
                        _lastMousePt = gpt
                        AddAction(New RecordedAction() With {.Type = ActionType.MouseMove, .X = gpt.X, .Y = gpt.Y})
                    End If
                Case &H201 : AddAction(New RecordedAction() With {.Type = ActionType.MouseLeftClick,  .X = gpt.X, .Y = gpt.Y})
                Case &H204 : AddAction(New RecordedAction() With {.Type = ActionType.MouseRightClick, .X = gpt.X, .Y = gpt.Y})
            End Select
        End If
        Return CallNextHookEx(_mouseHook, nCode, wParam, lParam)
    End Function

    Private Function KeyHookCB(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        If nCode >= 0 AndAlso _recording Then
            Dim kb As KBDLLHOOKSTRUCT = Marshal.PtrToStructure(Of KBDLLHOOKSTRUCT)(lParam)
            If wParam.ToInt32() = 256 Then   ' WM_KEYDOWN
                Dim vk As Keys = CType(kb.vkCode, Keys)
                If vk = Keys.F12 Then
                    Me.BeginInvoke(Sub() OnStopRecord(Nothing, Nothing))
                Else
                    Dim aType As ActionType = ActionType.KeyPress
                    If vk >= Keys.D1 AndAlso vk <= Keys.D6 Then aType = ActionType.UseSkill
                    If vk = Keys.F Then aType = ActionType.Pickup
                    Dim pt As New POINT() : GetCursorPos(pt)
                    Dim gpt As System.Drawing.Point = ScreenToGame(pt.X, pt.Y)
                    AddAction(New RecordedAction() With {
                        .Type = aType, .KeyCode = CInt(kb.vkCode),
                        .KeyName = vk.ToString(), .X = gpt.X, .Y = gpt.Y})
                End If
            End If
        End If
        Return CallNextHookEx(_keyboardHook, nCode, wParam, lParam)
    End Function
#End Region

#Region "Replay"
    Private Sub OnStartReplay(s As Object, e As EventArgs)
        If _replaying OrElse _currentSession.Actions.Count = 0 Then
            Log("[!!] Khong co actions.") : Return
        End If
        _replaying = True : btnReplay.Enabled = False : btnStopReplay.Enabled = True
        Dim loops As Integer = CInt(nudLoopCount.Value)
        _replayThread = New Thread(Sub() ReplayWorker(loops)) With {.IsBackground = True}
        _replayThread.Start()
        Log(String.Format("[>>] Replay {0} actions x{1} loops.", _currentSession.Actions.Count, loops))
    End Sub

    Private Sub OnStopReplay(s As Object, e As EventArgs)
        _replaying = False : btnReplay.Enabled = True : btnStopReplay.Enabled = False
        Log("[||] Da dung replay.")
    End Sub

    Private Sub ReplayWorker(loops As Integer)
        Try
            For lp As Integer = 1 To loops
                If Not _replaying Then Exit For
                Dim rc As RECT = GetGameRect()
                For Each a As RecordedAction In _currentSession.Actions
                    If Not _replaying Then Exit For
                    If a.DelayMs > 0 Then Thread.Sleep(CInt(Math.Min(a.DelayMs, 5000)))
                    Dim absX As Integer = If(chkRelative.Checked, rc.Left + a.X, a.X)
                    Dim absY As Integer = If(chkRelative.Checked, rc.Top  + a.Y, a.Y)
                    Select Case a.Type
                        Case ActionType.MouseMove        : If _inputMode = InputMode.RealMouse Then SetCursorPos(absX, absY)
                        Case ActionType.MouseLeftClick   : DoClick(absX, absY, True)
                        Case ActionType.MouseRightClick  : DoClick(absX, absY, False)
                        Case ActionType.Pickup           : DoKey(Keys.F)
                        Case ActionType.UseSkill, ActionType.KeyPress
                            DoKey(CType(a.KeyCode, Keys))
                        Case ActionType.Delay            : Thread.Sleep(CInt(Math.Min(a.DelayMs, 10000)))
                    End Select
                Next
                If lp < loops AndAlso _replaying Then Thread.Sleep(300)
            Next
        Catch ex As Exception
            Log("[Replay] Loi: " & ex.Message)
        Finally
            Me.BeginInvoke(Sub()
                _replaying = False : btnReplay.Enabled = True : btnStopReplay.Enabled = False
                Log("[Replay] Hoan thanh.")
            End Sub)
        End Try
    End Sub
#End Region

#Region "Pick Region Overlay"
    Private Sub OnPickBtn(s As Object, e As EventArgs)
        _pickMode = CType(s, Button).Tag.ToString()
        If _pickOverlay IsNot Nothing Then _pickOverlay.Close()
        Dim over As New Form() With {.FormBorderStyle = FormBorderStyle.None, .BackColor = Color.Lime,
                                      .Opacity = 0.35,
                                      .TopMost = True, .Cursor = Cursors.Cross,
                                      .ShowInTaskbar = False, .KeyPreview = True,
                                      .WindowState = FormWindowState.Maximized}
        _pickOverlay = over
        Dim startPt As System.Drawing.Point : Dim dragging As Boolean = False
        Dim selRect As System.Drawing.Rectangle
        AddHandler over.MouseDown, Sub(os, me2)
            If me2.Button = MouseButtons.Left Then startPt = me2.Location : dragging = True
        End Sub
        AddHandler over.MouseMove, Sub(os, me2)
            If dragging Then selRect = MakeRect(startPt, me2.Location) : over.Invalidate()
        End Sub
        AddHandler over.Paint, Sub(os, pe)
            If dragging Then
                pe.Graphics.FillRectangle(New SolidBrush(Color.FromArgb(60, 0, 120, 255)), selRect)
                pe.Graphics.DrawRectangle(New Pen(Color.White, 2), selRect)
            End If
        End Sub
        AddHandler over.MouseUp, Sub(os, me2)
            If me2.Button = MouseButtons.Left AndAlso dragging Then
                dragging = False : selRect = MakeRect(startPt, me2.Location)
                Dim screenRect As System.Drawing.Rectangle = over.RectangleToScreen(selRect)
                over.Close()
                If selRect.Width > 4 AndAlso selRect.Height > 4 Then
                    ApplyPick(_pickMode, screenRect)
                End If
            End If
        End Sub
        AddHandler over.KeyDown, Sub(os, ke)
                                     If ke.KeyCode = Keys.Escape Then over.Close()
                                 End Sub
        over.Show()
        over.Activate()
        over.Focus()
    End Sub

    Private Sub ApplyPick(mode As String, r As System.Drawing.Rectangle)
        Dim offX As Integer = 0, offY As Integer = 0
        If cmbProcess.SelectedItem IsNot Nothing Then
            Dim procs() As Process = Process.GetProcessesByName(cmbProcess.SelectedItem.ToString())
            If procs.Length > 0 Then
                Dim hWnd As IntPtr = procs(0).MainWindowHandle
                Dim origin As New POINT() : ClientToScreen(hWnd, origin)
                offX = origin.X : offY = origin.Y
            End If
        End If
        Dim relX As Integer = r.X - offX : Dim relY As Integer = r.Y - offY
        Dim set4 As Action(Of TextBox, TextBox, TextBox, TextBox) =
            Sub(tx, ty, tw, th)
                tx.Text = relX.ToString() : ty.Text = relY.ToString()
                tw.Text = r.Width.ToString() : th.Text = r.Height.ToString()
            End Sub
        Select Case mode
            Case "hp"    : set4(txtHpX,    txtHpY,    txtHpW,    txtHpH)
            Case "mp"    : set4(txtMpX,    txtMpY,    txtMpW,    txtMpH)
            Case "nl"    : set4(txtNlX,    txtNlY,    txtNlW,    txtNlH)
            Case "exp"   : set4(txtExpX,   txtExpY,   txtExpW,   txtExpH)
            Case "coord" : set4(txtCoordX, txtCoordY, txtCoordW, txtCoordH)
            Case "map"   : set4(txtMapX,   txtMapY,   txtMapW,   txtMapH)
        End Select
        Log(String.Format("[Pick] {0}: screen=({1},{2}) -> rel=({3},{4}) {5}x{6}",
            mode.ToUpper(), r.X, r.Y, relX, relY, r.Width, r.Height))
    End Sub

    Private Shared Function MakeRect(a As System.Drawing.Point, b As System.Drawing.Point) As System.Drawing.Rectangle
        Return New System.Drawing.Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y))
    End Function
#End Region

#Region "Preview"
    Private Sub UpdatePreview(rc As RECT)
        Try
            Dim zone As String = If(cmbPreviewZone.SelectedItem IsNot Nothing, cmbPreviewZone.SelectedItem.ToString(), "hp")
            Dim txX, txY, txW, txH As TextBox
            Select Case zone
                Case "hp"    : txX = txtHpX    : txY = txtHpY    : txW = txtHpW    : txH = txtHpH
                Case "mp"    : txX = txtMpX     : txY = txtMpY    : txW = txtMpW    : txH = txtMpH
                Case "nl"    : txX = txtNlX     : txY = txtNlY    : txW = txtNlW    : txH = txtNlH
                Case "exp"   : txX = txtExpX    : txY = txtExpY   : txW = txtExpW   : txH = txtExpH
                Case "coord" : txX = txtCoordX  : txY = txtCoordY : txW = txtCoordW : txH = txtCoordH
                Case "map"   : txX = txtMapX    : txY = txtMapY   : txW = txtMapW   : txH = txtMapH
                Case Else    : Return
            End Select
            Dim absX As Integer = rc.Left + ParseInt(txX.Text)
            Dim absY As Integer = rc.Top  + ParseInt(txY.Text)
            Dim w    As Integer = ParseInt(txW.Text)
            Dim h    As Integer = ParseInt(txH.Text)
            If w < 2 OrElse h < 2 Then Return
            Dim bmp As Bitmap = CaptureRegion(absX, absY, w, h)
            If picPreview.InvokeRequired Then
                picPreview.Invoke(Sub()
                    If picPreview.Image IsNot Nothing Then picPreview.Image.Dispose()
                    picPreview.Image = bmp
                End Sub)
            Else
                If picPreview.Image IsNot Nothing Then picPreview.Image.Dispose()
                picPreview.Image = bmp
            End If
        Catch : End Try
    End Sub
#End Region

#Region "Bitmap Helpers"
    Private Shared Function CaptureRegion(x As Integer, y As Integer, w As Integer, h As Integer) As Bitmap
        Dim bmp As New Bitmap(Math.Max(1, w), Math.Max(1, h))
        Using g As Graphics = Graphics.FromImage(bmp)
            g.CopyFromScreen(x, y, 0, 0, bmp.Size)
        End Using
        Return bmp
    End Function

    Private Shared Function BitmapToMat(bmp As Bitmap) As Mat
        Dim rect As New Rectangle(0, 0, bmp.Width, bmp.Height)
        Dim bd   As BitmapData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            Dim rowBytes As Integer = bmp.Width * 4
            Dim mat As New Mat(bmp.Height, bmp.Width, MatType.CV_8UC4)
            For row As Integer = 0 To bmp.Height - 1
                Dim src As IntPtr = IntPtr.Add(bd.Scan0, row * bd.Stride)
                Dim dst As IntPtr = IntPtr.Add(mat.Data,  row * rowBytes)
                Dim buf(rowBytes - 1) As Byte
                Marshal.Copy(src, buf, 0, rowBytes)
                Marshal.Copy(buf, 0, dst, rowBytes)
            Next
            Dim bgr As New Mat()
            Cv2.CvtColor(mat, bgr, ColorConversionCodes.BGRA2BGR)
            mat.Dispose()
            Return bgr
        Finally
            bmp.UnlockBits(bd)
        End Try
    End Function
#End Region

#Region "JSON Save / Load"
    Private Shared ReadOnly _jsonOpts As New JsonSerializerOptions() With {
        .WriteIndented = True,
        .PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }
    Private Shared ReadOnly _jsonOptsInit As Boolean = InitJsonOpts()
    Private Shared Function InitJsonOpts() As Boolean
        _jsonOpts.Converters.Add(New JsonStringEnumConverter())
        Return True
    End Function

    Private Sub OnSaveJson(s As Object, e As EventArgs)
        Dim fn As String = txtJsonPath.Text.Trim()
        If Not fn.EndsWith(".json") Then fn &= ".json"
        Try
            If Not Directory.Exists(_profilesDir) Then Directory.CreateDirectory(_profilesDir)
            Dim filePath As String = Path.Combine(_profilesDir, Path.GetFileName(fn))
            File.WriteAllText(filePath, JsonSerializer.Serialize(_currentSession, _jsonOpts), Encoding.UTF8)
            Log("[JSON] Da luu: " & filePath)
            LoadProfileList()
        Catch ex As Exception
            Log("[!!] Luu loi: " & ex.Message)
        End Try
    End Sub

    Private Sub OnLoadJson(s As Object, e As EventArgs)
        Dim fn As String = txtJsonPath.Text.Trim()
        If Not fn.EndsWith(".json") Then fn &= ".json"
        Dim filePath As String = Path.Combine(_profilesDir, Path.GetFileName(fn))
        If Not File.Exists(filePath) Then Log("[!!] Khong tim thay: " & filePath) : Return
        Try
            _currentSession = JsonSerializer.Deserialize(Of ReplayFile)(File.ReadAllText(filePath, Encoding.UTF8), _jsonOpts)
            If _currentSession Is Nothing Then _currentSession = New ReplayFile()
            nudLoopCount.Value = Math.Max(1, Math.Min(9999, _currentSession.LoopCount))
            UpdateActionList()
            Log(String.Format("[JSON] Da tai: {0}  ({1} actions)", fn, _currentSession.Actions.Count))
        Catch ex As Exception
            Log("[!!] Tai loi: " & ex.Message)
        End Try
    End Sub

    Private Sub OnProfileSelected(s As Object, e As EventArgs)
        If cmbProfiles.SelectedItem IsNot Nothing Then txtJsonPath.Text = cmbProfiles.SelectedItem.ToString()
    End Sub

    Private Sub OnDeleteProfile(s As Object, e As EventArgs)
        If cmbProfiles.SelectedItem Is Nothing Then Return
        Dim fn As String = cmbProfiles.SelectedItem.ToString()
        If MessageBox.Show("Xoa: " & fn & "?", "Xac nhan", MessageBoxButtons.YesNo) = DialogResult.Yes Then
            Dim filePath As String = Path.Combine(_profilesDir, fn)
            If File.Exists(filePath) Then File.Delete(filePath)
            LoadProfileList()
        End If
    End Sub

    Private Sub LoadProfileList()
        cmbProfiles.Items.Clear()
        If Not Directory.Exists(_profilesDir) Then Return
        For Each f As String In Directory.GetFiles(_profilesDir, "*.json")
            cmbProfiles.Items.Add(Path.GetFileName(f))
        Next
        If cmbProfiles.Items.Count > 0 Then cmbProfiles.SelectedIndex = 0
    End Sub
#End Region

#Region "Browse Templates"
    Private Sub OnBrowseTemplates(s As Object, e As EventArgs)
        Using dlg As New FolderBrowserDialog()
            dlg.Description  = "Chon thu muc templates (chua mobs/, items/, npcs/)"
            dlg.SelectedPath = _templateDir
            If dlg.ShowDialog() = DialogResult.OK Then
                _templateDir = dlg.SelectedPath : txtTemplateDir.Text = _templateDir
                Log("[Tpl] " & _templateDir)
            End If
        End Using
    End Sub
#End Region

#Region "Helpers"
    Private Function ParseInt(s As String) As Integer
        Dim v As Integer = 0 : Integer.TryParse(s.Trim(), v) : Return v
    End Function

    Private Sub Log(msg As String)
        If txtLog Is Nothing Then Return
        If txtLog.InvokeRequired Then txtLog.Invoke(Sub() Log(msg)) : Return
        txtLog.AppendText(String.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, msg, Environment.NewLine))
        txtLog.ScrollToCaret()
        If txtLog.Lines.Length > 200 Then
            txtLog.Select(0, txtLog.GetFirstCharIndexFromLine(txtLog.Lines.Length - 150))
            txtLog.SelectedText = ""
        End If
    End Sub
#End Region

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _replaying = False : RemoveHooks()
        tmrStat.Stop() : tmrDetect.Stop() : tmrAuto.Stop()
        If _replayThread IsNot Nothing AndAlso _replayThread.IsAlive Then _replayThread.Join(500)
        If _ocrEngine IsNot Nothing Then _ocrEngine.Dispose()
        If picPreview.Image IsNot Nothing Then picPreview.Image.Dispose()
        MyBase.OnFormClosing(e)
    End Sub
End Class

Module Program
    <STAThread>
    Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.Run(New VLTKBot())
    End Sub
End Module
