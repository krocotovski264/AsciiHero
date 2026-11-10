# AsciiHero

Новая работа идёт в Unity. Каталог `AsciiHero/` — прототип beta 0.8 (браузер и WebView), его оставляем как образец поведения, картинки и движения.

## Версия редактора

Оба разработчика ставят **одну и ту же** сборку:

| | |
|---|---|
| Линейка | Unity 6.3 LTS |
| Версия | **6000.3.28f1** |
| Changeset | `8f082d2fe476` |
| Поддержка | до декабря 2027 |
| Рендер | URP 17.3 |
| Ввод | Input System |

Unity 6.0 LTS к ноябрю 2026 уже без поддержки. Другой патч 6.3 переписывает `ProjectSettings/ProjectVersion.txt`, и проект начинает расходиться.

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
