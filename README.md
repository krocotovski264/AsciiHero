# AsciiHero

Новая работа идёт в Unity. Каталог `AsciiHero/` — прототип beta 0.8 (браузер и WebView), его оставляем как образец поведения, картинки и движения.

Документация по геймдизайну и разработке лежит отдельным репозиторием: [krocotovski264/AsciiHero-docs](https://github.com/krocotovski264/AsciiHero-docs).

## Версия редактора

В команде стоят Unity 6 и 2022.3 LTS. Этот репозиторий открывается в **Unity 6**: одна сборка на двоих.

| | |
|---|---|
| Линейка | Unity 6.3 LTS |
| Версия | **6000.3.28f1** |
| Changeset | `8f082d2fe476` |
| Поддержка | до декабря 2027 |
| Рендер | URP 17.3 |
| Ввод | Input System |

2022.3 LTS остаётся для других проектов. Папка `Unity/` в формате Unity 6, общая работа в 2022.3 её разъедет: у редакторов разные `ProjectSettings`. Поддержка 2022.3 закончилась в мае 2025. Для AsciiHero оба ставят 6000.3.28f1. Другой патч Unity 6 переписывает `ProjectSettings/ProjectVersion.txt`, и проект начинает расходиться.

Установка через Unity Hub (ссылку можно вставить в адресную строку проводника или открыть из браузера):

`unityhub://6000.3.28f1/8f082d2fe476`

Модули: Windows Build Support (IL2CPP). Android Build Support — когда дойдём до телефона.

## Как подключиться

Нужны [Git](https://git-scm.com/) и [Git LFS](https://git-lfs.com/) (на этой машине уже стоит LFS 3.8).

```powershell
git lfs install
git clone <url-репозитория> AsciiHero
cd AsciiHero
git lfs pull
```

В Unity Hub: **Add project from disk** и выбрать папку `Unity` (не корень репозитория). Редактор — `6000.3.28f1`.

Первый запуск создаёт URP-ассет, сцену `Assets/Scenes/Main.unity` и `ProjectSettings`. Тот, кто открыл проект первым, коммитит эти файлы и `Packages/packages-lock.json`, чтобы у второго они уже были.

Игровой код — `Unity/Assets/Scripts/`.

## Что уже в Unity

Порт прототипа beta 0.8 на C# и URP: редактор героя (пол, рост, телосложение, 5 комплектов брони, 6 видов оружия),
трактир «Пьяный дракон», ASCII-рендер на GPU, Motion Matching на mocap CMU, Foot IK, камера сверху, стик и свайп.
Сцена не нужна: `Game` создаёт всё сам после загрузки любой сцены (стандартные камера и свет отключаются).

| Файл | Что делает |
|---|---|
| `AHData.cs` | загрузка `Resources/human.bytes` (тело MakeHuman) и `mm.bytes` (база движений), перевод в систему координат Unity |
| `AHHuman.cs`, `AHPose.cs` | скелет, скиннинг, одежда-оболочки по телу, базовые позы рук |
| `AHMesh.cs`, `AHMaterials.cs` | генератор примитивов, палитра материалов с ASCII-id |
| `AHCharacter.cs` | сборка героя: броня, волосы, оружие, щит |
| `AHMotion.cs` | Motion Matching и Foot IK |
| `AHAnimatorDriver.cs` | режим Animator Controller: параметры Speed / LeanAngle / TurnAngle и т. д. |
| `AHGlyphs.cs`, `AHAscii.cs` + `Resources/Shaders` | модели собраны из символов: точки рядами по поверхности, каждая — ровный символ лицом к камере, яркость от света и глубины; запасной режим — сетка экрана |
| `AHTavern.cs` | трактир, свет, коллизии, срез стен |
| `Game.cs`, `AHInput.cs` | режимы, интерфейс, ввод (Input System) |
| `Editor/AnimatorProjectPatcher.cs` | **Tools → Patch Project Locomotion**: клипы из mocap + Animator Controller |
| `Editor/CIBuild.cs` | сборка APK (меню **Tools → Build Android APK** и GitHub Actions) |

## APK через GitHub Actions

`.github/workflows/android.yml` собирает APK на серверах GitHub при каждом пуше в `main` или `unity-port`
(и вручную: Actions → Android APK → Run workflow). Готовый `AsciiHero.apk` без архива лежит в **Releases → «Последняя сборка APK»** и заменяется при каждой сборке.

Один раз нужно добавить секреты репозитория (Settings → Secrets and variables → Actions → New repository secret):

| Секрет | Что положить |
|---|---|
| `UNITY_EMAIL` | почта аккаунта Unity |
| `UNITY_PASSWORD` | пароль аккаунта Unity |
| `UNITY_LICENSE` | всё содержимое файла лицензии `Unity_lic.ulf` |

Файл лицензии появляется после входа в Unity Hub с активированной Personal-лицензией:
Windows — `C:\ProgramData\Unity\Unity_lic.ulf`, macOS — `/Library/Application Support/Unity/Unity_lic.ulf`.
Открыть блокнотом и вставить текст целиком. Без секретов сборка останавливается на первом шаге с понятной ошибкой.

## Что не коммитить

`Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, `*.csproj`, `*.sln` — это локальная генерация Unity. В `.gitignore` они уже закрыты.

Ключ подписи старого APK (`AsciiHero/pipeline/apk/hero.jks`) в репозиторий не входит. Его передают отдельно.

Крупные бинарники (текстуры, fbx, звук, apk) идут через Git LFS. После клона без `git lfs pull` вместо моделей будут текстовые указатели.

## Ветки

Работаем от `main`. Перед началом сессии — `git pull`. Сцены и префабы лучше не править одновременно: в `.gitattributes` для них указан UnityYAMLMerge, но сам merge-driver каждый ставит локально, когда Unity уже установлен:

```powershell
git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
git config merge.unityyamlmerge.name "Unity SmartMerge"
```

Файлы `.meta` коммитятся вместе с ассетами. Без них у второго разработчика поедут ссылки.

## Сторонние данные

- Анимации ударов мечом — Bandai Namco Research Motiondataset-1 (`slash_normal_001/002`), лицензия **CC BY-NC 4.0**: только некоммерческое использование, с указанием авторства. © Bandai Namco Research Inc. https://github.com/BandaiNamcoResearchInc/Bandai-Namco-Research-Motiondataset
- Ходьба, бег, стойка, блок мечом (клип 76_04) — CMU Graphics Lab Motion Capture Database (mocap.cs.cmu.edu), свободное использование.
