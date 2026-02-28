#!/bin/bash

usage() { echo "Usage: $0 [-g <\"C:\SteamLibrary\steamapps\common\White Knuckle\">] [-b <\"C:\...\r2modmanPlus-local\WhiteKnuckle\profiles\Default\BepInEx\">]" 1>&2; exit 1; }

while getopts ":g:b:" o; do
    case "${o}" in
        g)
            g=${OPTARG}
            ;;
        b)
            b=${OPTARG}
            ;;
        *)
            usage
            ;;
    esac
done
shift $((OPTIND-1))

if [ -z "${g}" ] || [ -z "${b}" ]; then
    usage
fi

GAMEDATA_PATH="./lib/GameData"
BEPINEX_PATH="./lib/BepInEx"
UNITY_PATH="./lib/Unity"

WK_PATH=$(cygpath -u "${g}\White Knuckle_Data\Managed")
BEPINEX_SOURCE_PATH=$(cygpath -u "${b}\core")

mkdir "${GAMEDATA_PATH}"
mkdir "${UNITY_PATH}"
mkdir "${BEPINEX_PATH}"

GAMEDATA_FILES=(
"${WK_PATH}/ALINE.dll"
"${WK_PATH}/Assembly-CSharp.dll"
"${WK_PATH}/Assembly-CSharp-firstpass.dll"
"${WK_PATH}/DarkMachineUI.dll"
"${WK_PATH}/Facepunch.Steamworks.Win64.dll"
"${WK_PATH}/Newtonsoft.Json.dll"
)
cp -t "${GAMEDATA_PATH}/" "${GAMEDATA_FILES[@]}"

UNITY_FILES=(
"${WK_PATH}/Unity.Mathematics.Extensions.Hybrid.dll"
"${WK_PATH}/Unity.Mathematics.Extensions.dll"
"${WK_PATH}/Unity.Mathematics.dll"
"${WK_PATH}/Unity.Postprocessing.Runtime.dll"
"${WK_PATH}/Unity.TextMeshPro.dll"
"${WK_PATH}/UnityEngine.AssetBundleModule.dll"
"${WK_PATH}/UnityEngine.CoreModule.dll"
"${WK_PATH}/UnityEngine.InputLegacyModule.dll"
"${WK_PATH}/UnityEngine.ParticleSystemModule.dll"
"${WK_PATH}/UnityEngine.PhysicsModule.dll"
"${WK_PATH}/UnityEngine.TextRenderingModule.dll"
"${WK_PATH}/UnityEngine.UI.dll"
"${WK_PATH}/UnityEngine.dll"

)
cp -t "${UNITY_PATH}/" "${UNITY_FILES[@]}"

cp -a -t "${BEPINEX_PATH}/" "${BEPINEX_SOURCE_PATH}/."

rm "${BEPINEX_PATH}/0Harmony20.dll"