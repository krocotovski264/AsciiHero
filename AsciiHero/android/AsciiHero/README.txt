ASCII Hero — бета 0.1 (создание персонажа)

Как собрать APK:
1. Установите Android Studio (бесплатно, developer.android.com/studio).
2. File > Open > выберите папку AsciiHero. Дождитесь синхронизации Gradle (первый раз скачает зависимости).
3. Build > Build App Bundle(s) / APK(s) > Build APK(s).
4. Готовый файл: app/build/outputs/apk/debug/app-debug.apk — скиньте на телефон и установите
   (разрешите установку из неизвестных источников).
   Или подключите телефон по USB с включённой отладкой и нажмите Run ▶.

Вся игра лежит в app/src/main/assets/index.html — это тот же файл, что и веб-версия.
Чтобы обновить игру, просто замените index.html и пересоберите.

Credits:
- Human body: MakeHuman (CC0) — makehumancommunity.org
- Motion capture: CMU Graphics Lab Motion Capture Database, mocap.cs.cmu.edu (created with funding from NSF EIA-0196217); BVH conversion by B. Hahne (cgspeed)
- Motion Matching runtime: own implementation of the algorithm by S. Clavet (Ubisoft) as described in D. Holden's open-source Motion-Matching (MIT) / "Code vs Data Driven Displacement"
