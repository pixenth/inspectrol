; Inspectrol installer (NSIS 3). Built by build/release.ps1.

Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma

!macro Require NAME
  !ifndef ${NAME}
    !error "Define ${NAME} on the makensis command line"
  !endif
!macroend
!insertmacro Require VERSION
!insertmacro Require PUBLISH_DIR
!insertmacro Require LICENSE_FILE
!insertmacro Require ICON
!insertmacro Require OUTPUT

!define APP_NAME "Inspectrol"
!define APP_EXE "Inspectrol.exe"
!define PUBLISHER "Pavel Akulichev"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

Name "${APP_NAME}"
OutFile "${OUTPUT}"

; Per-user install: no administrator rights and no UAC prompt.
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"

BrandingText " "
ShowInstDetails nevershow
ShowUninstDetails nevershow

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Russian"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "LegalCopyright" "© 2026 ${PUBLISHER}"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "FileDescription" "Установка ${APP_NAME}"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "ProductVersion" "${VERSION}"

; A running exe cannot be opened for writing, which detects an open program without plugins.
!macro WaitForAppToClose
  StrCpy $1 0
  ${Do}
    ${IfNot} ${FileExists} "$INSTDIR\${APP_EXE}"
      ${Break}
    ${EndIf}
    ClearErrors
    FileOpen $0 "$INSTDIR\${APP_EXE}" a
    ${IfNot} ${Errors}
      FileClose $0
      ${Break}
    ${EndIf}
    ; A silent update is started by the program itself, which closes right after; give it time to exit.
    ${If} ${Silent}
    ${AndIf} $1 < 40
      IntOp $1 $1 + 1
      Sleep 250
      ${Continue}
    ${EndIf}
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Inspectrol сейчас открыт. Закройте программу и нажмите «Повтор»." /SD IDCANCEL IDRETRY +2
    Abort
  ${Loop}
!macroend

; /RUN starts the program after installation. The update started from the program passes it together with /S.
Function .onInstSuccess
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/RUN" $R1
  ${IfNot} ${Errors}
    Exec '"$INSTDIR\${APP_EXE}"'
  ${EndIf}
FunctionEnd

Section
  !insertmacro WaitForAppToClose

  ; Remove the previous version so no stale files stay next to the new ones.
  ${If} ${FileExists} "$INSTDIR\${APP_EXE}"
    SetOutPath "$TEMP"
    RMDir /r "$INSTDIR"
  ${EndIf}

  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"
  File "/oname=LICENSE.txt" "${LICENSE_FILE}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

  ${GetSize} "$INSTDIR" "/S=0K" $1 $2 $3
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  !insertmacro WaitForAppToClose

  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"

  ; Settings, the log and downloaded updates, which can take gigabytes. Saved recordings live elsewhere.
  RMDir /r "$LOCALAPPDATA\${APP_NAME}"

  ; $INSTDIR is wherever Uninstall.exe runs from. Wipe it only if it really holds the program, so a copied
  ; uninstaller cannot erase an unrelated folder such as the desktop.
  ${If} ${FileExists} "$INSTDIR\${APP_EXE}"
    SetOutPath "$TEMP"
    RMDir /r "$INSTDIR"
  ${Else}
    Delete "$INSTDIR\Uninstall.exe"
  ${EndIf}
SectionEnd
