import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode, urlsplit
from urllib.request import Request, urlopen
import zipfile

if __package__:
    from .ipa_local_visuals import append_local_visuals, prepare_local_interface, prepare_local_visuals
else:
    from ipa_local_visuals import append_local_visuals, prepare_local_interface, prepare_local_visuals


PROJECT = Path(__file__).resolve().parents[1]
WORKFLOW = "ios-ipa-windows-export.yml"
BUNDLE_ID = "com.ygoportable.lotd"
KOREA_TIME = datetime.timezone(datetime.timedelta(hours=9))
ROSTER_FILES = ("opponents_db.json", "opponent_deck_levels.json")


class BuildError(Exception):
    pass


class GitHubHttpError(BuildError):
    def __init__(self, method, path, status):
        super().__init__(f"GitHub 요청 실패 ({method} {path}, HTTP {status}).")
        self.status = status


def git(*args, check=True, stdin=None):
    return subprocess.run(
        ["git", "-c", "credential.interactive=false", *args], cwd=PROJECT,
        input=stdin, encoding="utf-8", errors="replace", capture_output=True,
        env={**os.environ, "GIT_TERMINAL_PROMPT": "0"}, timeout=90, check=check,
    )


def local_build_folder(key):
    result = git("config", "--local", "--get", key, check=False)
    if result.returncode == 1:
        return None
    result.check_returncode()
    value = result.stdout.strip()
    return Path(value).expanduser() if value else None


class GitHub:
    def __init__(self, repository):
        self.repository = repository
        self.api = f"https://api.github.com/repos/{repository}"
        self.workflow = f"/actions/workflows/{WORKFLOW}"
        self.token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
        if not self.token:
            credential = git(
                "credential", "fill", check=False,
                stdin=f"protocol=https\nhost=github.com\npath={repository}.git\n\n",
            )
            if credential.returncode == 0:
                self.token = next(
                    (line.partition("=")[2] for line in credential.stdout.splitlines() if line.startswith("password=")),
                    None,
                )
        if not self.token:
            raise BuildError("Git for Windows의 GitHub 로그인 정보가 필요합니다. GitHub에 로그인한 뒤 다시 실행하세요.")

    def request(self, path, *, method="GET", data=None, headers=None, timeout=90):
        url = self.api + path if path.startswith("/") or not path else path
        host = urlsplit(url)
        if host.scheme != "https" or host.netloc not in ("api.github.com", "uploads.github.com"):
            raise BuildError("GitHub 전송 주소를 확인할 수 없습니다.")
        request = Request(url, method=method, data=data, headers={
            "User-Agent": "YGO-Portable-IPA",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2026-03-10",
            **(headers or {}),
        })
        # The Actions artifact redirects to a storage URL, which needs no GitHub credential.
        request.add_unredirected_header("Authorization", f"Bearer {self.token}")
        attempts = 3 if method == "GET" else 1
        for attempt in range(attempts):
            try:
                return urlopen(request, timeout=timeout)
            except HTTPError as error:
                error.close()
                if error.code not in (429, 500, 502, 503, 504) or attempt == attempts - 1:
                    raise GitHubHttpError(method, host.path, error.code) from None
            except (URLError, TimeoutError, ConnectionError) as error:
                if attempt == attempts - 1:
                    raise BuildError(f"GitHub 연결 실패 ({method} {host.path}, {type(error).__name__}).") from None
            time.sleep(3)

    def json(self, path, *, method="GET", payload=None):
        data = json.dumps(payload).encode("utf-8") if payload is not None else None
        with self.request(path, method=method, data=data, headers={"Content-Type": "application/json"}) as response:
            body = response.read()
        return json.loads(body) if body else None

    def run_workflow(self, branch, revision, asset_id="", timeout_minutes=45):
        dispatch = self.json(
            self.workflow + "/dispatches", method="POST",
            payload={"ref": branch, "inputs": {"xcode_asset_id": str(asset_id)}},
        )
        if not dispatch or not dispatch.get("workflow_run_id"):
            raise BuildError("GitHub가 실행 번호를 반환하지 않았습니다. Actions의 최근 실행을 확인하세요.")
        run_id = dispatch["workflow_run_id"]
        link = f"https://github.com/{self.repository}/actions/runs/{run_id}"
        print(f"    GitHub Actions {link}")
        started = time.monotonic()
        deadline = started + timeout_minutes * 60
        previous_status = None
        while time.monotonic() < deadline:
            try:
                run = self.json(f"/actions/runs/{run_id}")
            except GitHubHttpError as error:
                # Dispatch can return the ID before the run is visible. Keep the Xcode asset available.
                if error.status != 404 or previous_status is not None or time.monotonic() - started >= 60:
                    raise
                time.sleep(3)
                continue
            if run["head_sha"] != revision:
                raise BuildError("빌드 중 원격 브랜치가 변경되었습니다. CMD를 다시 실행하세요.")
            status = run["status"]
            if status != previous_status:
                print(f"    GitHub 상태 {status}")
                previous_status = status
            if status == "completed":
                if run["conclusion"] != "success":
                    raise BuildError(f"GitHub 빌드 결과 {run['conclusion']}. {link}")
                return run
            time.sleep(15)
        raise BuildError(f"GitHub 빌드 대기 시간을 초과했습니다. {link}")

    def download_artifact(self, run, name, destination):
        artifacts = self.json(f"/actions/runs/{run['id']}/artifacts")["artifacts"]
        artifact = next((a for a in artifacts if a["name"] == name and not a["expired"]), None)
        if artifact is None:
            raise BuildError(f"GitHub 빌드에서 {name} 파일을 찾을 수 없습니다.")
        digest = hashlib.sha256()
        with self.request(f"/actions/artifacts/{artifact['id']}/zip", timeout=150) as response, destination.open("wb") as output:
            for chunk in iter(lambda: response.read(1024 * 1024), b""):
                output.write(chunk)
                digest.update(chunk)
        if destination.stat().st_size != artifact["size_in_bytes"]:
            raise BuildError("다운로드한 GitHub 파일의 크기가 일치하지 않습니다.")
        if artifact.get("digest") and artifact["digest"] != f"sha256:{digest.hexdigest()}":
            raise BuildError("다운로드한 GitHub 파일의 SHA-256이 일치하지 않습니다.")
        return artifact["id"]

    def upload_export(self, draft, archive):
        url = draft["upload_url"].split("{", 1)[0] + "?" + urlencode({"name": archive.name})
        size = archive.stat().st_size
        with archive.open("rb") as source, self.request(
            url, method="POST", data=source, timeout=480,
            headers={"Content-Type": "application/gzip", "Content-Length": str(size)},
        ) as response:
            asset = json.load(response)
        if asset["state"] != "uploaded" or asset["size"] != size:
            raise BuildError("전송한 Xcode 압축 파일의 크기가 일치하지 않습니다.")
        return asset["id"]


def prepare():
    if os.name != "nt" or not shutil.which("git") or not shutil.which("tar.exe"):
        raise BuildError("Windows와 Git for Windows, Windows 기본 tar.exe가 필요합니다.")
    version_file = PROJECT / "ProjectSettings/ProjectVersion.txt"
    editor_version = re.search(r"^m_EditorVersion: (\S+)", version_file.read_text(encoding="utf-8-sig"), re.M)
    if not editor_version:
        raise BuildError("ProjectVersion.txt에서 Unity 버전을 확인할 수 없습니다.")
    version = editor_version[1]
    hub_editor = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Unity" / "Hub" / "Editor"
    editor = Path(os.environ.get("YGO_UNITY_EDITOR") or hub_editor / version / "Editor" / "Unity.exe")
    if not editor.is_file() or not (editor.parent / "Data/PlaybackEngines/iOSSupport").is_dir():
        raise BuildError(f"Unity {version}과 iOS Build Support가 필요합니다. 다른 경로에 설치했다면 YGO_UNITY_EDITOR에 Unity.exe 경로를 지정하세요.")

    origin = git("remote", "get-url", "origin").stdout.strip()
    match = re.fullmatch(r"(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)([\w.-]+/[\w.-]+?)(?:\.git)?/?", origin)
    if not match:
        raise BuildError("origin에 GitHub 저장소 주소를 설정하세요.")
    github = GitHub(match[1])
    repository = github.json("")
    if repository.get("permissions", {}).get("push") is False:
        raise BuildError("이 GitHub 저장소의 쓰기 권한이 있는 계정으로 로그인하세요.")
    branch = repository["default_branch"]
    revision = github.json(f"/branches/{quote(branch, safe='')}")["commit"]["sha"]
    if github.json(github.workflow)["state"] != "active":
        raise BuildError("GitHub Actions의 Windows IPA 빌드 워크플로를 활성화하세요.")

    if git("cat-file", "-e", f"{revision}^{{commit}}", check=False).returncode != 0:
        git("fetch", "--quiet", "--no-tags", "origin", f"refs/heads/{branch}")
    remote_inputs = ("NativeEngine", f".github/workflows/{WORKFLOW}", "Tools/build-ios-ipa.sh")
    changed = git("diff", "--name-only", revision, "--", *remote_inputs).stdout.strip()
    new = git("ls-files", "--others", "--exclude-standard", "--", *remote_inputs).stdout.strip()
    if changed or new:
        raise BuildError(f"GitHub에서 컴파일하는 엔진이나 빌드 설정의 변경을 {branch}에 먼저 반영하세요.\n{changed}\n{new}".rstrip())
    target = re.compile(r"^\s*iOSTargetOSVersionString:\s*(\S+)", re.M)
    local_settings = (PROJECT / "ProjectSettings/ProjectSettings.asset").read_text(encoding="utf-8")
    remote_settings = git("show", f"{revision}:ProjectSettings/ProjectSettings.asset").stdout
    if target.findall(local_settings) != target.findall(remote_settings):
        raise BuildError(f"iOS 최소 버전 설정을 {branch}에 먼저 반영하세요.")
    return editor.resolve(), github, branch, revision


def export_project(editor, snapshot, library, output, log):
    plugin = snapshot / "Assets/Plugins/iOS/libygo_native.a"
    plugin.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(library) as artifact:
        data = artifact.read("libygo_native.a")
    if not data.startswith(b"!<arch>\n"):
        raise BuildError("iOS 듀얼 엔진 파일이 올바른 정적 라이브러리가 아닙니다.")
    plugin.write_bytes(data)
    print(f"    Unity 빌드 로그 {log}")
    result = subprocess.run(
        [str(editor), "-batchmode", "-nographics", "-quit", "-job-worker-count", "4",
         "-projectPath", str(snapshot), "-logFile", str(log), "-buildTarget", "iOS",
         "-executeMethod", "YGOPortable.Editor.YgoIosBuilder.Export",
         "-ygoIosOutput", str(output), "-ygoIosBundleId", BUNDLE_ID],
        creationflags=subprocess.CREATE_NO_WINDOW, timeout=1800,
    )
    if result.returncode or not (output / "Unity-iPhone.xcodeproj/project.pbxproj").is_file():
        raise BuildError(f"Unity에서 iOS 프로젝트를 내보내지 못했습니다. 로그 파일 {log}")


def verify_ipa(ipa, snapshot):
    with zipfile.ZipFile(ipa) as package:
        if package.testzip() is not None:
            raise BuildError("IPA 압축 파일이 손상되었습니다.")
        apps = [n for n in package.namelist()
                if re.fullmatch(r"Payload/[^/]+\.app/Info\.plist", n)]
        if len(apps) != 1:
            raise BuildError("IPA 안에서 아이폰 앱을 찾을 수 없습니다.")
        app = apps[0].removesuffix("/Info.plist")
        settings = plistlib.loads(package.read(apps[0]))
        if settings.get("CFBundleIdentifier") != BUNDLE_ID:
            raise BuildError("IPA의 번들 ID가 기존 앱과 일치하지 않습니다.")
        binaries = (settings["CFBundleExecutable"], "Frameworks/UnityFramework.framework/UnityFramework")
        for name in binaries:
            with package.open(f"{app}/{name}") as executable:
                if executable.read(8) != b"\xcf\xfa\xed\xfe\x0c\x00\x00\x01":
                    raise BuildError(f"IPA에서 ARM64 바이너리를 확인할 수 없습니다. {name}")
        for name in ROSTER_FILES:
            if package.read(f"{app}/Data/Raw/{name}") != (snapshot / "Assets/StreamingAssets" / name).read_bytes():
                raise BuildError(f"IPA에 포함된 {name} 파일이 작업 폴더와 다릅니다.")


def ipa_filename(run_number, stamp, full=False):
    edition = "-full" if full else ""
    return f"YGO-Portable-unsigned{edition}-run{run_number}-{stamp}-KST.ipa"


def save_verified_ipa(source, destination):
    if destination.exists():
        raise BuildError(f"같은 이름의 IPA가 이미 있습니다. {destination}")
    temporary = tempfile.NamedTemporaryFile(prefix=f".{destination.stem}-", suffix=".part",
                                            dir=destination.parent, delete=False)
    partial = Path(temporary.name)
    try:
        with temporary, source.open("rb") as data:
            shutil.copyfileobj(data, temporary, length=4 * 1024 * 1024)
            temporary.flush()
            if temporary.tell() != source.stat().st_size:
                raise BuildError(f"IPA 복사본의 크기가 원본과 일치하지 않습니다. {destination}")
        partial.rename(destination)
    finally:
        partial.unlink(missing_ok=True)


def build(editor, github, branch, revision, scratch, local_cutins=None, local_interface=None, drive_folder=None):
    visuals = {}
    if local_cutins:
        visuals.update(prepare_local_visuals(local_cutins))
        portraits = sum(name.startswith("CutIns/") for name in visuals)
        message = f"    최종 IPA에 로컬 컷인 {portraits}개와 전체 일러스트 팩을 포함합니다."
        if "overframeart.pack" in visuals:
            message += " 원본 오버프레임 앞면 팩도 포함합니다."
        print(message)
    if local_interface:
        interface = prepare_local_interface(local_interface)
        visuals.update(interface)
        print(f"    최종 IPA에 로컬 슬리브·페이즈·카드 아이콘 {len(interface)}개를 포함합니다.")
    snapshot = scratch / "Project"
    for name in ("Assets", "Packages", "ProjectSettings"):
        shutil.copytree(PROJECT / name, snapshot / name)

    print("[2/5] GitHub에서 iOS 듀얼 엔진을 컴파일합니다.")
    run = github.run_workflow(branch, revision)
    library = scratch / "native.zip"
    github.download_artifact(run, "YGO-iOS-native-library", library)

    print("[3/5] Unity에서 현재 작업 파일을 iOS 프로젝트로 내보냅니다.")
    stamp = datetime.datetime.now(KOREA_TIME).strftime("%Y%m%d-%H%M%S")
    log = PROJECT / "Logs" / f"ipa-{stamp}-{scratch.name}.log"
    log.parent.mkdir(exist_ok=True)
    xcode = scratch / "Xcode"
    export_project(editor, snapshot, library, xcode, log)
    archive = scratch / "xcode-project.tar.gz"
    subprocess.run(["tar.exe", "-czf", str(archive), "-C", str(xcode), "."], check=True)

    print("[4/5] GitHub의 Mac에서 IPA를 만듭니다.")
    draft = github.json("/releases", method="POST", payload={
        "tag_name": f"ios-xcode-transfer-{stamp}-{scratch.name}",
        "target_commitish": revision,
        "name": f"Temporary iOS build transfer {stamp}",
        "draft": True,
    })
    if not draft["draft"]:
        raise BuildError("Xcode 전송에 필요한 GitHub 임시 초안을 만들지 못했습니다.")
    try:
        asset_id = github.upload_export(draft, archive)
        run = github.run_workflow(branch, revision, asset_id, timeout_minutes=90)
    finally:
        try:
            github.json(f"/releases/{draft['id']}", method="DELETE")
        except BuildError:
            print(f"    임시 Xcode 전송 초안을 정리하지 못했습니다. {draft['html_url']}", file=sys.stderr)

    print("[5/5] IPA를 내려받아 검사합니다.")
    artifact_zip = scratch / "ipa.zip"
    artifact_id = github.download_artifact(run, f"YGO-Portable-iOS-unsigned-{run['run_number']}", artifact_zip)
    ipa = scratch / "YGO-Portable-unsigned.ipa"
    with zipfile.ZipFile(artifact_zip) as artifact, artifact.open(ipa.name) as source, ipa.open("wb") as output:
        shutil.copyfileobj(source, output)
    if visuals:
        append_local_visuals(ipa, visuals)
    verify_ipa(ipa, snapshot)
    out_dir = PROJECT / "Builds/iOS/IPA"
    out_dir.mkdir(parents=True, exist_ok=True)
    destination = out_dir / ipa_filename(run["run_number"], stamp, full=bool(local_cutins))
    save_verified_ipa(ipa, destination)
    print(f"\n완료 {destination}")
    artifact_label = "GitHub 기본 빌드" if visuals else "GitHub 다운로드"
    print(f"{artifact_label} https://github.com/{github.repository}/actions/runs/{run['id']}/artifacts/{artifact_id}")
    print("아이폰에서 기존 방식으로 서명해 설치하는 미서명 IPA입니다.")
    if drive_folder is not None:
        if not drive_folder.is_dir():
            raise BuildError(f"로컬 IPA 저장 완료. Google Drive 폴더를 연결한 뒤 복사하세요. {drive_folder}")
        drive_copy = drive_folder / destination.name
        save_verified_ipa(destination, drive_copy)
        print(f"Google Drive 동기화 폴더에 저장 {drive_copy}")


def main():
    parser = argparse.ArgumentParser(description="Windows Unity와 GitHub Actions로 테스트용 IPA를 만듭니다.")
    parser.add_argument("--check", action="store_true", help="빌드 없이 설치 상태와 GitHub 연결을 확인합니다.")
    parser.add_argument("--local-cutins", type=Path, metavar="DUEL_VISUALS",
                        help="PC의 DuelVisuals에서 컷인과 대응 일러스트를 최종 미서명 IPA에 추가합니다.")
    parser.add_argument("--local-interface", type=Path, metavar="DUEL_VISUALS",
                        help="PC의 DuelVisuals에서 슬리브·페이즈·카드 아이콘을 최종 미서명 IPA에 추가합니다.")
    options = parser.parse_args()
    if sys.version_info < (3, 9):
        raise BuildError("Python 3.9 이상이 필요합니다.")
    print("[1/5] Unity iOS 모듈과 GitHub 연결을 확인합니다.")
    editor, github, branch, revision = prepare()
    print(f"    {github.repository} / {branch} / {revision[:7]}")
    drive_folder = local_build_folder("ygo.ipaDriveFolder")
    local_cutins = options.local_cutins or local_build_folder("ygo.ipaDuelVisuals")
    if local_cutins is not None:
        print(f"    풀버전 에셋 폴더 {local_cutins}")
    if drive_folder is not None:
        drive_status = "연결됨" if drive_folder.is_dir() else "연결 필요"
        print(f"    Google Drive 폴더 {drive_folder} ({drive_status})")
    if options.check:
        print("준비 확인 완료")
        return
    temp_root = Path(tempfile.gettempdir()).resolve()
    scratch = Path(tempfile.mkdtemp(prefix="ygo-ipa-", dir=temp_root)).resolve()
    try:
        build(editor, github, branch, revision, scratch, local_cutins, options.local_interface, drive_folder)
    finally:
        # Remove only the temporary directory allocated for this build.
        if scratch.resolve().parent == temp_root and scratch.name.startswith("ygo-ipa-"):
            try:
                shutil.rmtree(scratch)
            except OSError:
                print(f"    임시 빌드 폴더를 정리하지 못했습니다. {scratch}", file=sys.stderr)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit("IPA 빌드를 취소했습니다.")
    except (BuildError, OSError, KeyError, ValueError, zipfile.BadZipFile, subprocess.SubprocessError) as error:
        sys.exit(f"IPA 빌드 실패. {error}")
