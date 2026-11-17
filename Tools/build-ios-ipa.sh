#!/usr/bin/env bash
set -euo pipefail

fail() {
    printf '::error::%s\n' "$1" >&2
    exit 1
}

read_signing_profile() {
    [[ -n "${IOS_PROVISIONING_PROFILE_BASE64:-}" ]] ||
        fail "Add IOS_PROVISIONING_PROFILE_BASE64 for a signed build."
    printf '%s' "$IOS_PROVISIONING_PROFILE_BASE64" | base64 -D > "$temp_directory/profile.mobileprovision"
    security cms -D -i "$temp_directory/profile.mobileprovision" -o "$temp_directory/profile.plist"

    local profile_data
    profile_data="$(python3 - "$temp_directory/profile.plist" "$IOS_BUNDLE_IDENTIFIER" "$IOS_SIGNING" <<'PY'
import datetime
import plistlib
import sys

path, bundle, signing = sys.argv[1:]
with open(path, "rb") as profile_file:
    profile = plistlib.load(profile_file)
team = profile["TeamIdentifier"][0]
prefix = profile["ApplicationIdentifierPrefix"][0]
entitlements = profile["Entitlements"]
devices = profile.get("ProvisionedDevices")
is_development = entitlements.get("get-task-allow", False)

if profile["ExpirationDate"].replace(tzinfo=datetime.timezone.utc) <= datetime.datetime.now(datetime.timezone.utc):
    sys.exit("The iOS provisioning profile has expired.")
if entitlements.get("application-identifier") != f"{prefix}.{bundle}":
    sys.exit("The provisioning profile must match the selected bundle identifier.")
if "iOS" not in profile.get("Platform", []):
    sys.exit("The provisioning profile must support iOS.")
if signing == "debugging" and not (is_development and devices):
    sys.exit("Choose an iOS development provisioning profile with registered devices.")
if signing == "release-testing" and (is_development or not devices):
    sys.exit("Choose an iOS Ad Hoc provisioning profile with registered devices.")
if signing == "app-store-connect" and (is_development or devices or profile.get("ProvisionsAllDevices")):
    sys.exit("Choose an iOS App Store provisioning profile.")
print(team, profile["UUID"])
PY
)" || fail "Could not read the selected iOS provisioning profile."
    read -r team_id profile_uuid <<< "$profile_data"
    [[ "$team_id" =~ ^[A-Z0-9]{10}$ && "$profile_uuid" =~ ^[[:xdigit:]-]{36}$ ]] ||
        fail "The provisioning profile has invalid signing metadata."
}

case "${IOS_SIGNING:-}" in
    unsigned) ;;
    debugging) signing_identity="Apple Development" ;;
    release-testing|app-store-connect) signing_identity="Apple Distribution" ;;
    *) fail "Choose unsigned, debugging, release-testing or app-store-connect." ;;
esac

[[ "${IOS_BUNDLE_IDENTIFIER:-}" =~ ^[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)+$ ]] ||
    fail "Enter a valid iOS bundle identifier."

temp_directory="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/ygo-ios.XXXXXX")"
keychain=""
new_profile=""
existing_keychains=()
cleanup() {
    if [[ -n "$keychain" ]]; then
        security list-keychains -d user -s ${existing_keychains[@]+"${existing_keychains[@]}"} || true
        security delete-keychain "$keychain" || true
    fi
    if [[ -n "$new_profile" ]]; then
        rm -f -- "$new_profile"
    fi
    rm -rf -- "$temp_directory"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

if [[ "$IOS_SIGNING" != unsigned ]]; then
    read_signing_profile
fi

case "${1:-}" in
    signing-settings)
        if [[ "$IOS_SIGNING" == unsigned ]]; then
            printf 'signing_args=\n'
        else
            printf 'signing_args=-ygoIosTeamId %s -ygoIosProvisioningProfile %s -ygoIosSigningIdentity %s\n' \
                "$team_id" "$profile_uuid" "${signing_identity/ /}"
        fi
        exit 0
        ;;
    package) ;;
    *) fail "Use signing-settings or package." ;;
esac

project="${IOS_XCODE_PATH:?}/Unity-iPhone.xcodeproj"
archive="$temp_directory/YGO-Portable.xcarchive"
output_path="$PWD/${IOS_IPA_PATH:?}"
[[ -d "$project" ]] || fail "Unity did not export the Unity-iPhone Xcode project."
mkdir -p "$output_path"

archive_args=(-project "$project" -scheme Unity-iPhone -configuration Release
    -destination generic/platform=iOS -archivePath "$archive" -derivedDataPath "$temp_directory/DerivedData")

if [[ "$IOS_SIGNING" == unsigned ]]; then
    xcodebuild "${archive_args[@]}" archive CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO
    shopt -s nullglob
    apps=("$archive"/Products/Applications/*.app)
    [[ "${#apps[@]}" == 1 ]] || fail "Expected one iOS application in the Xcode archive."
    mkdir -p "$temp_directory/Payload"
    ditto "${apps[0]}" "$temp_directory/Payload/${apps[0]##*/}"
    (
        cd "$temp_directory"
        zip -q -r -y YGO-Portable-unsigned.ipa Payload
    )
    mv -f "$temp_directory/YGO-Portable-unsigned.ipa" "$output_path/"
    exit 0
fi

[[ -n "${IOS_CERTIFICATE_BASE64:-}" && -n "${IOS_CERTIFICATE_PASSWORD:-}" ]] ||
    fail "Add IOS_CERTIFICATE_BASE64 and IOS_CERTIFICATE_PASSWORD for a signed build."
printf '%s' "$IOS_CERTIFICATE_BASE64" | base64 -D > "$temp_directory/certificate.p12"
keychain_password="$(openssl rand -hex 24)"
keychain_path="$temp_directory/ios-build.keychain-db"
while IFS= read -r path; do
    existing_keychains+=("$path")
done < <(security list-keychains -d user | sed -E 's/^[[:space:]]*"//; s/"$//')
security create-keychain -p "$keychain_password" "$keychain_path"
keychain="$keychain_path"
security unlock-keychain -p "$keychain_password" "$keychain"
security set-keychain-settings -lut 5400 "$keychain"
security import "$temp_directory/certificate.p12" -P "$IOS_CERTIFICATE_PASSWORD" -A -t cert -f pkcs12 -k "$keychain"
security set-key-partition-list -S apple-tool:,apple: -s -k "$keychain_password" "$keychain" >/dev/null
security list-keychains -d user -s "$keychain" ${existing_keychains[@]+"${existing_keychains[@]}"}
unset IOS_CERTIFICATE_BASE64 IOS_CERTIFICATE_PASSWORD IOS_PROVISIONING_PROFILE_BASE64 keychain_password
rm -f -- "$temp_directory/certificate.p12"

profiles_directory="$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles"
mkdir -p "$profiles_directory"
profile_path="$profiles_directory/$profile_uuid.mobileprovision"
if [[ ! -e "$profile_path" ]]; then
    cp "$temp_directory/profile.mobileprovision" "$profile_path"
    new_profile="$profile_path"
fi

options="$temp_directory/ExportOptions.plist"
python3 - "$options" "$IOS_SIGNING" "$IOS_BUNDLE_IDENTIFIER" "$team_id" "$profile_uuid" "$signing_identity" <<'PY'
import plistlib
import sys

path, method, bundle, team, profile, certificate = sys.argv[1:]
with open(path, "wb") as options:
    plistlib.dump({
        "method": method,
        "destination": "export",
        "signingStyle": "manual",
        "signingCertificate": certificate,
        "teamID": team,
        "provisioningProfiles": {bundle: profile},
        "manageAppVersionAndBuildNumber": False,
    }, options)
PY

xcodebuild "${archive_args[@]}" archive
xcodebuild -exportArchive -archivePath "$archive" -exportOptionsPlist "$options" -exportPath "$temp_directory/export"
shopt -s nullglob
packages=("$temp_directory"/export/*.ipa)
[[ "${#packages[@]}" == 1 ]] || fail "Expected one IPA from Xcode export."
mv -f "${packages[0]}" "$output_path/YGO-Portable.ipa"
