@echo off
setlocal
where py.exe >nul 2>&1
if errorlevel 1 goto python_on_path
py -3 -c "import sys" >nul 2>&1
if errorlevel 1 goto python_on_path
py -3 -X utf8 -u "%~dp0Tools\build_ipa_windows.py" %*
set "YGO_IPA_EXIT=%errorlevel%"
goto done

:python_on_path
where python.exe >nul 2>&1
if errorlevel 1 (
    echo Install Python 3.9 or later for Windows and add it to PATH.
    set "YGO_IPA_EXIT=1"
    goto done
)
python -X utf8 -u "%~dp0Tools\build_ipa_windows.py" %*
set "YGO_IPA_EXIT=%errorlevel%"

:done
if "%~1"=="" pause
exit /b %YGO_IPA_EXIT%
