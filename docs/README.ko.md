# Astral Party Character Select 1.2.0

[English](README.en.md) · [日本語](README.ja.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.zh-TW.md)

Steam 국제판용 BepInEx 6 IL2CPP 플러그인입니다. 캐릭터 태그, 정렬, 랜덤 선택, 팀·맵 기반 추천을 추가합니다.

## 설치와 사용

1. BepInEx 6 IL2CPP를 설치하고 게임을 한 번 실행합니다.
2. 게임을 종료하고 ZIP의 `BepInEx` 폴더를 게임 실행 파일이 있는 폴더에 합칩니다. 이 플러그인의 DLL은 한 사본만 설치합니다.
3. 캐릭터 상세정보 왼쪽에서 태그를 지정합니다. `전체 설정`에서는 기본 조합 51개를 편집하거나 새 조합을 만들 수 있습니다. 도감의 맵 상세정보에서는 [협동 광기 기준 맵 프리셋 10개](MapRecommendations.ko.md)의 추천 순서와 제외를 수정할 수 있습니다.

설정이 없는 첫 실행에서는 첫 협동 캐릭터 선택창에서 보유와 협동 레벨 6이 확인된 캐릭터에만 [기본 자리·역할 태그](CharacterDefaults.ko.md)를 한 번 적용합니다. 코마치·펭·메가스는 잠재 해방이 확인될 때만 초기 태그를 부여합니다. 기존 태그와 이후 수동 설정은 유지합니다.

태그 버튼은 해당 태그, 랜덤 버튼은 태그가 하나라도 있는 캐릭터를 대상으로 합니다. 버튼별로 한 바퀴 동안 미선택 후보부터 무작위로 시도한 뒤, 이미 선택된 후보까지 시도합니다. 한 바퀴가 끝나면 다시 시작합니다. 중복 선택 가능 여부는 게임이 결정합니다. 최종 확정은 게임의 확인 버튼에서 합니다.

정렬은 **기본 → 자리 → 역할 → 사용자 → 수동** 순이고, 유효한 입력이 없는 사용자·수동 모드는 건너뜁니다. 기본은 진입 시 게임 순서와 카드 슬롯 배치를 복원합니다. 카드 하단의 색은 우선순위 앞 네 태그에 대응하며, 여러 태그에 해당하면 인원이 적은 그룹을 우선합니다.

## 추천 기준 문서

- [기본 자리·역할과 초기 태그 판정](CharacterDefaults.ko.md)
- [광기 맵별 추천·제외 근거](MapRecommendations.ko.md)
- [조건 추천 알고리즘, 점수와 긴급 판정](RecommendationAlgorithm.ko.md)

## 설정

게임을 종료한 뒤 `BepInEx/config/astral-party.character-select.cfg`를 편집합니다.

| 항목 | 내용 |
|---|---|
| `Language` | `Auto`, `Korean`, `English`, `Japanese`, `SimplifiedChinese`, `TraditionalChinese`. 자동 감지는 한글 패치를 포함한 게임 표시 텍스트를 사용합니다. |
| `Mode` | `Default`, `Seat`, `Role`, `Custom`, `Manual`. |
| `CustomOrder` | 태그 키 또는 표시명. 예시 `P1, Card, P2, Support`. 빠진 태그는 기본 순서로 이어집니다. |
| `ManualOrder` | 쉼표로 구분한 캐릭터 ID. 빠진 캐릭터는 게임 순서로 이어집니다. |
| `Enabled` | 조건 선택 버튼을 표시합니다. |
| `[긴급 추천]` | 기본값 OFF. 캐릭터 상세정보의 `전체 설정 → 긴급 추천`에서 켜고 끄거나 역할당 목표 인원(1~4명)과 확인할 역할 태그를 바꿉니다. 캐릭터의 후보 포함 여부는 `개별 설정`에서 바꿉니다. |
| `Combinations`, `CombinationNames`, `DisabledCombinations`, `FixedSeatCombinations` | 조합 편집기가 저장하는 구성원, 이름, 미사용 여부, 자리 고정 설정입니다. 게임 내 `전체 설정`에서 수정할 수 있습니다. |
| `[태그 이름]`, `[캐릭터 ID]` | 태그의 표시 이름과 태그별 캐릭터 ID입니다. 태그 버튼의 기본 키는 `P1`~`P4`, `Dealer`, `Support`, `Card`, `Tank`입니다. |
| `RecommendationDiagnostics` | 기본값 `false`. `true`이면 조건 선택 클릭 시 팀·후보 ID를 BepInEx 로그에 기록합니다. |
| `[Map <ID>]` | `Preferred`는 맵 추천 순서, `Excluded`는 조건 및 긴급 추천 제외 목록입니다. |

설정 섹션과 키는 기존 파일과 호환됩니다. 설정 설명은 한국어로 제공됩니다. 화면의 기본 버튼과 메시지는 5개 언어를 지원하며, 사용자가 지정한 조합명과 태그 이름은 입력 그대로 표시합니다. 한국어 기본 조합명은 다른 UI 언어에서 캐릭터 이름으로 표시합니다. `[캐릭터 목록]`은 게임에서 확인한 ID와 이름을 기록합니다.

맵 이름과 캐릭터 이름은 `#` 주석으로 표시합니다. 설정을 수정할 때는 오른쪽의 숫자 ID 목록을 바꾸면 됩니다. 이름의 언어는 판정에 영향을 주지 않습니다.

## 소스 빌드

BepInEx 6 IL2CPP로 게임을 한 번 실행해 `BepInEx/interop`을 생성한 뒤 다음 명령을 실행합니다.

```powershell
dotnet build AstralParty.CharacterSelect.csproj -c Release "-p:GameDir=C:/Games/Astral Party"
```

결과는 `bin/Release/net6.0/AstralParty_CharacterSelect.dll`에 생성됩니다.
