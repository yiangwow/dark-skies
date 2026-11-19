#!/usr/bin/env bash
# Run Unity EditMode tests headless from WSL.
# REQUIREMENT: the Unity Editor must NOT have this project open.
set -uo pipefail

UNITY="/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe"
PROJ_WIN='C:\Users\computer\alien-lane2'
PROJ_WSL="/mnt/c/Users/computer/alien-lane2"
RESULTS_WIN="${PROJ_WIN}\\TestResults\\editmode.xml"
LOG_WIN="${PROJ_WIN}\\TestResults\\editmode.log"
RESULTS_WSL="${PROJ_WSL}/TestResults/editmode.xml"
LOG_WSL="${PROJ_WSL}/TestResults/editmode.log"

mkdir -p "${PROJ_WSL}/TestResults"
rm -f "${RESULTS_WSL}"

timeout -k 30 540 "${UNITY}" -batchmode -projectPath "${PROJ_WIN}" -runTests \
  -testPlatform EditMode -testResults "${RESULTS_WIN}" -logFile "${LOG_WIN}"
UNITY_EXIT=$?

if [[ ${UNITY_EXIT} -eq 124 ]]; then
  echo "FAIL: Unity timed out after 540s (hung editor or modal dialog?)"
  exit 124
fi

if [[ ! -f "${RESULTS_WSL}" ]]; then
  echo "FAIL: no results file produced (unity exit ${UNITY_EXIT})."
  echo "Likely causes: editor has the project open, or compile error. Log tail:"
  tail -40 "${LOG_WSL}" 2>/dev/null
  grep -m5 "error CS" "${LOG_WSL}" 2>/dev/null
  exit 1
fi

python3 - "${RESULTS_WSL}" <<'EOF'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
print(f"result={root.get('result')} total={root.get('total')} "
      f"passed={root.get('passed')} failed={root.get('failed')}")
for tc in root.iter('test-case'):
    if tc.get('result') != 'Passed':
        print(f"  FAILED: {tc.get('fullname')}")
        msg = tc.find('.//message')
        if msg is not None and msg.text:
            print("    " + msg.text.strip().replace("\n", "\n    ")[:500])
sys.exit(0 if root.get('result') == 'Passed' else 1)
EOF
