; Inno Setup script for the SaveLocker Windows tray agent.
;
; Machine-wide install (requests UAC elevation up front): files go to
; C:\Program Files\SaveLocker. Auto-start is still the per-user HKCU\...\Run entry
; for the installing user (matching the in-app "Start with Windows" toggle). Produces
; an Add/Remove Programs entry whose uninstaller reverts every system change: it
; removes the Run entry (even if the app created it) and offers to delete the agent's
; config/state.
;
; Build:  publish the agent first, then compile this with ISCC.exe.
;         See installer\build-installer.ps1 (does both).

#define AppName "SaveLocker"
#ifndef AppVersion
  #define AppVersion "dev"
#endif
#define AppPublisher "SaveLocker"
#define AppExe "SaveLocker.Agent.exe"
; Path to the self-contained publish output (relative to this script).
#define PublishDir "..\src\Agent\bin\Release\net10.0-windows\win-x64\publish"
; Per-user Run key + the value name the in-app toggle (AutoStart.cs) uses.
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define RunValue "SaveLocker"

[Setup]
AppId={{3BF4B1EA-D263-4136-B1F8-92800DD752D9}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; Machine-wide install to Program Files needs admin; request elevation up front so the
; install can't fail half-way with "Access is denied" creating the program directory.
PrivilegesRequired=admin
OutputDir=dist
OutputBaseFilename=SaveLocker-Agent-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Shared with the agent's single-instance Mutex (Program.cs) so setup/uninstall can
; detect a running agent and ask the user to close it before replacing files.
AppMutex=SaveLocker.Agent
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

; Brand the wizard with SaveLocker colors.
; WizardSmallImageFile (55×58): top-right corner on all pages after Welcome.
WizardSmallImageFile=SaveLocker_WizardSmall.png
; WizardImageFile (164×314): left-panel background on Welcome and Finish pages.
WizardImageFile=SaveLocker_WizardBg.png
; Sidebar background if the image doesn't fill the panel (e.g. at non-96dpi).
; Value is a Windows COLORREF (BGR): #1E252A → $2A251E.
WizardImageBackColor=$2A251E

[Tasks]
Name: "autostart"; Description: "Start {#AppName} automatically when I log in"; GroupDescription: "Startup:"

[Files]
; Agent executable and runtime files (self-contained publish, no .NET runtime needed).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs ignoreversion
; Agent UI (React app served locally by the agent's built-in HTTP server).
; The MSBuild CopyAgentUiDistOnPublish target copies this into the publish directory.
Source: "{#PublishDir}\agent-ui\*"; DestDir: "{app}\agent-ui"; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist

[Icons]
; AppUserModelID is what gives the agent's toast notifications their name and icon: Windows takes a
; toast's header from the Start-menu shortcut that carries the toast's identity, and shows the bare
; identity string, with no icon, when there is none (verified on Windows 11 25H2). Must equal
; ToastPresenter.ProductAumid in src/Agent/ToastPresenter.cs — tests/run-appearance-consistency-tests
; fails if they drift.
; ToastActivatorCLSID makes the shortcut's identity a COM-activatable one — the supported route for a
; button that ACTS (tasks/notification-follow-ups). Must equal ToastActivation.ProductClsid, checked
; by the same test. (The tray registers it at runtime too; the shortcut carries it so an install that
; has never been run yet is still completable, and `{{` is Inno's literal `{`.)
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "SaveLocker"; \
    AppUserModelToastActivatorCLSID: "02C97085-6FAE-4DF4-8C52-2E05604D3AB0"

[Registry]
; Auto-start: written only if the user ticks the task; removed on uninstall. Same key
; and value the in-app toggle uses, so the two stay consistent.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunValue}"; \
    ValueData: """{app}\{#AppExe}"""; Tasks: autostart; Flags: uninsdeletevalue

; The toast activator (ToastActivation.cs writes these itself on every start; here so the very first
; toast after an install-for-all-users still has working buttons, and so the uninstaller takes them
; back). CustomActivator is what the shell resolves a foreground toast action through; LocalServer32
; is the process it starts when the tray is not running (see ToastActivationHost).
Root: HKCU; Subkey: "Software\Classes\CLSID\{{02C97085-6FAE-4DF4-8C52-2E05604D3AB0}"; \
    ValueType: string; ValueName: ""; ValueData: "SaveLocker toast activation"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{02C97085-6FAE-4DF4-8C52-2E05604D3AB0}\LocalServer32"; \
    ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" -ToastActivated"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\SaveLocker"; \
    ValueType: string; ValueName: "CustomActivator"; ValueData: "{{02C97085-6FAE-4DF4-8C52-2E05604D3AB0}"; \
    Flags: uninsdeletevalue

[Run]
; runasoriginaluser: start the tray agent de-elevated as the real user, not in the
; installer's admin context (a background tray app must not run elevated).
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName} now"; \
    Flags: nowait postinstall runasoriginaluser

[Code]
// ---------------------------------------------------------------------------------------
// Enrollment wizard page + post-install enroll.
//
// Enrollment works from the CLI (SaveLocker.Agent.exe enroll --url <join link>).
// This page just collects the join link and shells out to that command; it never
// reimplements enrollment. See docs/tasks/enrollment-options/ for the design — the
// load-bearing rules are enforced in ShouldSkipPage / DoEnroll below:
//
//   * The agent AUTO-UPDATES by running this installer /SILENT. That path must never
//     enroll: no wizard page is shown, ResolveEnrollUrl returns '', and even a stray
//     /ENROLL-URL is blocked by IsAlreadyEnrolled (re-enrolling would burn a code and
//     ROTATE this machine's key, orphaning its config).
//   * The elevated installer must NOT create %PROGRAMDATA%\SaveLocker: the de-elevated
//     tray agent has to rewrite config.json forever after. So the enroll runs via
//     ExecAsOriginalUser — the same reason [Run] uses runasoriginaluser — which creates
//     the dir as the interactive user with Modify rights.
//   * A failed enroll is non-fatal: the agent is installed and usable, just not enrolled.
//   * The join link is a live credential: it is passed to the agent as one argv element
//     and never written to disk by the installer.
// ---------------------------------------------------------------------------------------

var
  EnrollPage: TWizardPage;
  RbEnrollNow, RbEnrollLater: TNewRadioButton;
  EdJoinLink: TNewEdit;
  LblJoinHint: TNewStaticText;
  MemoPreview: TNewMemo;
  LblLater: TNewStaticText;
  EnrollPreviewOk: Boolean;

// Accepts exactly what the pairing page hands out: the server's /join/<code> link.
// Deliberately loose (scheme + /join/ segment); the agent's enroll --url is the
// authoritative parser and prints its own sentence for a link the server refuses.
function LooksLikeJoinLink(const Link: String): Boolean;
var
  lower: String;
begin
  lower := LowerCase(Trim(Link));
  Result := ((Pos('http://', lower) = 1) or (Pos('https://', lower) = 1))
            and (Pos('/join/', lower) > 0);
end;

// The authority from the link (host[:port]) for the preview, e.g. "192.168.1.10:5080".
function LinkHost(const Link: String): String;
var
  s, rest: String;
  i: Integer;
begin
  s := Trim(Link);
  i := Pos('://', s);
  if i = 0 then
  begin
    Result := s;
    exit;
  end;
  rest := Copy(s, i + 3, Length(s));
  i := Pos('/', rest);
  if i > 0 then rest := Copy(rest, 1, i - 1);
  Result := rest;
end;

// The code segment after the last "/join/" for the preview, e.g. "K7QM-4H2X".
function LinkCode(const Link: String): String;
var
  s: String;
  i: Integer;
begin
  s := Trim(Link);
  i := Pos('/join/', LowerCase(s));
  if i = 0 then
  begin
    Result := '';
    exit;
  end;
  s := Copy(s, i + 6, Length(s));
  while (Length(s) > 0) and ((s[Length(s)] = '/') or (s[Length(s)] = ' ')) do
    Delete(s, Length(s), 1);
  Result := UpperCase(s);
  if Length(Result) = 8 then
    Result := Copy(Result, 1, 4) + '-' + Copy(Result, 5, 4);
end;

procedure RefreshPreview;
var
  link: String;
begin
  EnrollPreviewOk := False;
  if (RbEnrollNow = nil) or (not RbEnrollNow.Checked) then
  begin
    MemoPreview.Text := '';
    exit;
  end;
  link := Trim(EdJoinLink.Text);
  if link = '' then
  begin
    MemoPreview.Text := 'Paste the join link from the console' + #13#10 +
                        '(Configuration -> Enroll a machine), or from the pairing page.' + #13#10#13#10 +
                        'No link yet? Choose "Skip" below and enroll from the agent''s Settings tab later.';
    exit;
  end;
  if not LooksLikeJoinLink(link) then
  begin
    MemoPreview.Text := 'That does not look like a join link.' + #13#10 +
                        'It should look like:  http://192.168.1.10:5080/join/K7QM-4H2X';
    exit;
  end;
  // The link is deliberately unsigned: the user is the trust anchor. Show the server so
  // they can catch a link that points at the wrong (or a malicious) server before joining.
  MemoPreview.Text :=
    'You are about to join:' + #13#10 +
    '    Server:   ' + LinkHost(link) + #13#10 +
    '    Code:     ' + LinkCode(link) + #13#10#13#10 +
    'Make sure the server address is one you trust before continuing.' + #13#10 +
    'The link works exactly once, and expires on its own.';
  EnrollPreviewOk := True;
end;

procedure EnrollOptionClicked(Sender: TObject);
var
  useNow: Boolean;
begin
  useNow := RbEnrollNow.Checked;
  EdJoinLink.Enabled := useNow;
  RefreshPreview;
end;

procedure EdJoinLinkChanged(Sender: TObject);
begin
  RefreshPreview;
end;

procedure InitializeWizard;
begin
  EnrollPage := CreateCustomPage(wpSelectDir, 'Enroll this machine',
    'Connect this machine to your SaveLocker server now, or do it later.');

  RbEnrollNow := TNewRadioButton.Create(WizardForm);
  RbEnrollNow.Parent := EnrollPage.Surface;
  RbEnrollNow.Left := 0;
  RbEnrollNow.Top := ScaleY(8);
  RbEnrollNow.Width := EnrollPage.SurfaceWidth;
  RbEnrollNow.Caption := 'Enroll this machine now (recommended)';
  RbEnrollNow.Checked := True;
  RbEnrollNow.OnClick := @EnrollOptionClicked;

  // One field, full width: the pairing page copies this link to the clipboard when its
  // Download button is pressed, so Ctrl+V here is the whole interaction.
  EdJoinLink := TNewEdit.Create(WizardForm);
  EdJoinLink.Parent := EnrollPage.Surface;
  EdJoinLink.Left := ScaleX(20);
  EdJoinLink.Top := ScaleY(34);
  EdJoinLink.Width := EnrollPage.SurfaceWidth - ScaleX(20);
  EdJoinLink.Height := ScaleY(23);
  EdJoinLink.OnChange := @EdJoinLinkChanged;

  LblJoinHint := TNewStaticText.Create(WizardForm);
  LblJoinHint.Parent := EnrollPage.Surface;
  LblJoinHint.Left := ScaleX(20);
  LblJoinHint.Top := ScaleY(60);
  LblJoinHint.Width := EnrollPage.SurfaceWidth - ScaleX(20);
  LblJoinHint.AutoSize := False;
  LblJoinHint.WordWrap := True;
  LblJoinHint.Height := ScaleY(16);
  LblJoinHint.Caption := 'Paste the join link the console created (it looks like http://your-server:5080/join/K7QM-4H2X).';

  MemoPreview := TNewMemo.Create(WizardForm);
  MemoPreview.Parent := EnrollPage.Surface;
  MemoPreview.Left := ScaleX(20);
  MemoPreview.Top := ScaleY(84);
  MemoPreview.Width := EnrollPage.SurfaceWidth - ScaleX(20);
  MemoPreview.Height := ScaleY(96);
  MemoPreview.ReadOnly := True;
  MemoPreview.ScrollBars := ssVertical;
  MemoPreview.WantReturns := False;
  MemoPreview.TabStop := False;

  RbEnrollLater := TNewRadioButton.Create(WizardForm);
  RbEnrollLater.Parent := EnrollPage.Surface;
  RbEnrollLater.Left := 0;
  RbEnrollLater.Top := ScaleY(192);
  RbEnrollLater.Width := EnrollPage.SurfaceWidth;
  RbEnrollLater.Caption := 'Skip - I''ll enroll later';
  RbEnrollLater.OnClick := @EnrollOptionClicked;

  LblLater := TNewStaticText.Create(WizardForm);
  LblLater.Parent := EnrollPage.Surface;
  LblLater.Left := ScaleX(20);
  LblLater.Top := ScaleY(212);
  LblLater.Width := EnrollPage.SurfaceWidth - ScaleX(20);
  LblLater.AutoSize := False;
  LblLater.WordWrap := True;
  LblLater.Height := ScaleY(32);
  LblLater.Caption := 'You can enroll anytime from the agent''s Settings tab (paste the join link there), ' +
                      'or by running SaveLocker.Agent.exe enroll --url <link>.';

  EnrollOptionClicked(nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  // CRITICAL: the agent's auto-update runs this installer with /SILENT. Inno still calls
  // NextButtonClick in silent mode even though no page is shown, and returning False there
  // (or popping a MsgBox) ABORTS the update. Never validate a silent install — just proceed.
  if WizardSilent then exit;
  if (EnrollPage <> nil) and (CurPageID = EnrollPage.ID) and RbEnrollNow.Checked then
  begin
    if Trim(EdJoinLink.Text) = '' then
    begin
      MsgBox('Paste the join link, or select "Skip - I''ll enroll later".',
             mbError, MB_OK);
      Result := False;
      exit;
    end;
    if not EnrollPreviewOk then
    begin
      MsgBox(MemoPreview.Text + #13#10 + #13#10 +
             'Fix the link or choose "Skip - I''ll enroll later".', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

// The link to enroll with: the wizard's field interactively, else the /ENROLL-URL switch
// (scripted/unattended). A silent auto-update passes neither -> '' -> no enroll.
function ResolveEnrollUrl: String;
begin
  if (not WizardSilent) and (EnrollPage <> nil) then
  begin
    if RbEnrollNow.Checked then
      Result := Trim(EdJoinLink.Text)
    else
      Result := '';
  end
  else
    Result := ExpandConstant('{param:ENROLL-URL|}');
end;

// True once this machine holds an API key. config.json is written with WhenWritingNull,
// so the "ApiKey" property is present only when set -> a reliable "already enrolled" guard.
function IsAlreadyEnrolled: Boolean;
var
  cfg: String;
  s: AnsiString;
begin
  Result := False;
  cfg := ExpandConstant('{commonappdata}\SaveLocker\config.json');
  if FileExists(cfg) and LoadStringFromFile(cfg, s) then
    Result := Pos('"ApiKey"', s) > 0;
end;

// On an upgrade/reinstall the machine is already enrolled, so there is nothing to ask:
// hide the enrollment page entirely. (Re-enrolling would rotate the machine's key; the
// deliberate path for that is the CLI / Settings tab, never a routine upgrade.) A fresh
// install has no config yet, so the page still shows there.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (EnrollPage <> nil) and (PageID = EnrollPage.ID) and IsAlreadyEnrolled;
end;

procedure DoEnroll;
var
  u, exe: String;
  rc: Integer;
begin
  u := ResolveEnrollUrl;
  if u = '' then exit;                  // skip / silent auto-update / nothing chosen
  if not LooksLikeJoinLink(u) then exit;
  if IsAlreadyEnrolled then exit;       // never re-enroll: it burns a code + rotates the key

  exe := ExpandConstant('{app}\{#AppExe}');
  // De-elevated, so %PROGRAMDATA%\SaveLocker is created by (and stays writable to) the
  // user the tray runs as. Only the link crosses the command line; the agent fetches the
  // policy itself and stores nothing but the machine key it gets back.
  if ExecAsOriginalUser(exe, 'enroll --url "' + u + '"', '', SW_HIDE,
                        ewWaitUntilTerminated, rc) then
  begin
    if (rc <> 0) and (not WizardSilent) then
      MsgBox('SaveLocker was installed, but enrolling this machine did not complete.'#13#10#13#10 +
             'You can enroll from the agent''s Settings tab, or run:'#13#10 +
             '    "' + exe + '" enroll --url <link>'#13#10#13#10 +
             'A common cause is an expired or already-used link - create a new one from the console.',
             mbInformation, MB_OK);
  end
  else if not WizardSilent then
    MsgBox('SaveLocker was installed, but the enrollment step could not be started.'#13#10 +
           'You can enroll later from the agent''s Settings tab.',
           mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // ssPostInstall runs after files are in place but before the [Run] tray launch, so the
  // agent comes up already enrolled. Enrollment failures never abort the install.
  if CurStep = ssPostInstall then
    DoEnroll;
end;

// On uninstall: always remove the Run entry (covers the case where the in-app toggle
// created it but the install-time task was not selected), then offer to delete the
// agent's config/state (%PROGRAMDATA%\SaveLocker — holds the API key + tracked games).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', '{#RunValue}');

  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{commonappdata}\SaveLocker');
    if DirExists(DataDir) then
    begin
      if MsgBox('Also remove your SaveLocker settings and API key?' + #13#10 +
                '(' + DataDir + ')' + #13#10#13#10 +
                'Choose No if you plan to reinstall and keep this machine''s registration.',
                mbConfirmation, MB_YESNO) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
