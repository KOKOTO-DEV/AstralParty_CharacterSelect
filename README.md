# Astral Party Character Select 1.1.0

Steam판 아스트랄 파티의 캐릭터 선택창에서 캐릭터를 태그로 분류하고 정렬하거나 무작위로 선택하는 BepInEx 플러그인입니다.

## 설치
자동설치
1. 게임을 종료하고 `AstralParty_CharacterSelect-1.2.0-Setup.exe`를 실행합니다. Steam에서 찾은 경로를 확인하거나 `AstralParty_INT.exe`가 있는 폴더를 선택합니다.
2. BepInEx가 없으면 설치 프로그램이 공식 Windows x64 IL2CPP 빌드 788을 다운로드하고 SHA-256을 확인한 뒤 설치합니다. 호환되는 BepInEx가 있으면 Character Select DLL만 설치하거나 교체합니다. 

수동설치
1. Astral Party에 BepInEx 6 IL2CPP가 설치되어 있는지 확인합니다.
2. 게임을 종료하고 배포 ZIP의 `BepInEx` 폴더를 게임 실행 파일이 있는 폴더에 합칩니다.


플러그인은 게임 시작 시 GitHub Releases에서 최신 정식 버전을 한 번 확인합니다. 
새 버전이 있으면 다운로드 페이지를 열지 묻습니다. 
업데이트 확인을 끄려면 설정 파일의 `[Updates]`에서 `CheckForUpdates = false`로 설정합니다.


## 사용


설정이 없는 첫 실행에서는 첫 협동 캐릭터 선택창에서 보유와 협동 레벨 6이 확인된 캐릭터에만 [기본 자리·역할 태그](docs/CharacterDefaults.ko.md)를 한 번 적용합니다. 
코마치·펭·메가스는 잠재 해방이 확인될 때만 초기 태그를 부여합니다. 기존 태그와 이후 수동 설정은 유지합니다.

태그 버튼은 해당 태그, 랜덤 버튼은 태그가 하나라도 있는 캐릭터를 대상으로 합니다. 
버튼별로 한 바퀴 동안 미선택 후보부터 무작위로 시도한 뒤, 이미 선택된 후보까지 시도합니다. 한 바퀴가 끝나면 다시 시작합니다. 

정렬은 **기본 → 자리 → 역할 → 사용자 → 수동** 순이고, 유효한 입력이 없는 사용자·수동 모드는 건너뜁니다. 
기본은 진입 시 게임 순서와 카드 슬롯 배치를 복원합니다. 
카드 하단의 색은 우선순위 앞 네 태그에 대응하며, 여러 태그에 해당하면 인원이 적은 그룹을 우선합니다.

## 추천 기준 문서

- [기본 자리·역할과 초기 태그 판정](docs/CharacterDefaults.ko.md)
- [광기 맵별 추천·제외 근거](docs/MapRecommendations.ko.md)
- [조건 추천 알고리즘, 점수와 긴급 판정](docs/RecommendationAlgorithm.ko.md)


<img width="482" height="365" alt="image" src="https://github.com/user-attachments/assets/647d4618-50f6-42be-8db6-4691500f2ec8" /><br />
- 캐릭터 상세정보 왼쪽의 태그를 클릭해 원하는 태그를 여러 개 지정합니다.
  
<img width="963" height="555" alt="image" src="https://github.com/user-attachments/assets/d922be6d-d662-47bb-ba2f-bfd8b892d85f" /><br />
- 매치 선택창에서 태그 버튼을 누르면 해당 그룹에서 한 명을 선택합니다. `랜덤`은 태그가 하나라도 달린 선택 가능 캐릭터 전체에서 뽑습니다. 최종 확정은 게임의 확인 버튼으로 합니다.
- `정렬`을 누르면 자리 우선, 역할 우선, 사용자 지정 태그 순서, 수동 캐릭터 순서가 차례로 바뀝니다.
- 카드 하단 색 띠는 정렬 기준의 앞 네 태그를 나타냅니다. 여러 색 대상에 해당하면 인원이 가장 적은 태그를 사용하고, 동률이면 정렬 순서를 따릅니다. 수동 모드에서는 사용자 지정 태그 순서로 색을 정합니다.

설정은 `BepInEx/config/astral-party.character-select.cfg`에 저장됩니다. 게임을 종료한 뒤 편집하세요.

```ini
[태그 이름]
Dealer = 딜러
Support = 서포터
Card = 카드특화
Tank = 탱커

[정렬]
Mode = Custom
CustomOrder = P1, P2, P3, P4, Dealer, Support, Card, Tank
ManualOrder = 102, 101
```

`Mode`는 `Seat`, `Role`, `Custom`, `Manual` 중 하나입니다. `[태그 이름]`에서 `P1`~`P4`와 역할 태그의 표시 이름을 바꿀 수 있습니다. `CustomOrder`는 고정 키와 표시 이름을 읽습니다. `ManualOrder`는 캐릭터 ID 순서이며, 빠진 ID는 게임 기본 순서대로 뒤에 붙습니다.

`[캐릭터 목록]`은 선택창에서 확인된 ID와 이름으로 갱신됩니다. 모드가 상세정보에서 본명까지 확인하면 괄호 안에 추가합니다.

## 1.1.0 정렬과 언어 설정
정렬 버튼은 기본 → 자리 → 역할 → 사용자 → 수동 순서로 전환합니다. 사용자 지정과 수동 순서가 비어 있으면 해당 모드를 건너뜁니다.
Mode = Default는 선택창에 처음 진입했을 때의 게임 순서를 복원합니다.
[일반]의 Language는 Auto, Korean, English, Japanese, SimplifiedChinese, TraditionalChinese를 지원합니다. Auto는 화면 텍스트를 통해 한글패치를 포함한 게임 표시 언어를 감지합니다.

## 소스 빌드

.NET SDK와 BepInEx 6 IL2CPP가 설치된 게임 폴더가 필요합니다. BepInEx로 게임을 한 번 실행해 `BepInEx/interop`을 생성한 뒤 아래 명령을 실행하세요. 경로는 실제 게임 폴더로 바꾸면 됩니다.

```powershell
dotnet build AstralParty.CharacterSelect.csproj -c Release "-p:GameDir=C:/Games/Astral Party"
```

빌드 결과는 `bin/Release/net6.0/AstralParty_CharacterSelect.dll`에 생성됩니다. Steam 국제판 게임 UI 구조를 사용하므로, 게임 업데이트 후에는 상세정보 탭과 매치 선택창 동작을 확인해 주세요.

