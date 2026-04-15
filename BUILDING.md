# Сборка DS1ParamEditor

## Основной проект (C#)

1. Откройте `DS1ParamEditor.sln` в Visual Studio 2022
2. Выберите конфигурацию Debug/Release и платформу Any CPU
3. Соберите решение (Ctrl+Shift+B)

## DS1ParamHelper.dll (опционально, для BonfireWarp)

### Зачем нужна эта DLL?

BonfireWarp требует выполнения кода в контексте процесса игры. Без DLL:
- ✅ Работает: чтение/запись параметров, позиция игрока, SetLastBonfire
- ❌ Не работает: автоматический BonfireWarp (можно телепортироваться вручную через меню)

С DLL:
- ✅ Всё работает, включая автоматический BonfireWarp

### Способ 1: Добавить проект в Solution (рекомендуется)

1. Откройте `DS1ParamEditor.sln`
2. ПКМ на Solution → Add → Existing Project
3. Выберите `DS1ParamHelper\DS1ParamHelper.vcxproj`
4. Выберите конфигурацию Release x64
5. Соберите проект DS1ParamHelper
6. DLL появится в `DS1ParamEditor\bin\Release\net8.0-windows\`

### Способ 2: Автоматическая сборка через bat

1. Перейдите в `DS1ParamHelper\`
2. Запустите `build-simple.bat`
3. Скрипт найдёт MSBuild и соберёт DLL

### Способ 3: Вручную

Откройте "x64 Native Tools Command Prompt for VS 2022":

```cmd
cd DS1ParamHelper
cl /LD /O2 /EHsc DS1ParamHelper.cpp /link /DEF:DS1ParamHelper.def /OUT:DS1ParamHelper.dll
copy DS1ParamHelper.dll ..\DS1ParamEditor\bin\Release\net8.0-windows\
```

## Требования

### Для основного проекта:
- Visual Studio 2022 или новее
- .NET 8.0 SDK
- Windows

### Для DS1ParamHelper.dll:
- Visual Studio 2022 с C++ Desktop Development
- Windows SDK 10.0
- MSVC v143 toolset

## Структура проектов

```
DS1ParamEditor/
├── DS1ParamEditor/          # Основной C# проект
│   ├── DS1ParamEditor.csproj
│   ├── Program.cs
│   ├── PlayerHook.cs
│   ├── InjectionHelper.cs   # DLL injection логика
│   └── ...
├── DS1ParamHelper/          # Опциональная C++ DLL
│   ├── DS1ParamHelper.vcxproj
│   ├── DS1ParamHelper.cpp
│   ├── DS1ParamHelper.def
│   └── build-simple.bat
└── DS1ParamEditor.sln       # Solution file
```

## Troubleshooting

### "cl не является внутренней или внешней командой"

Используйте "x64 Native Tools Command Prompt for VS 2022" вместо обычного cmd.
Или используйте Способ 1 (через Visual Studio).

### DLL не загружается в игру

- Проверьте, что DLL x64 (не x86)
- Запустите игру и редактор от администратора
- Проверьте антивирус (может блокировать injection)

### Игра крашится при BonfireWarp

- Убедитесь, что персонаж полностью загружен в игре
- Попробуйте вызвать BonfireWarp не в меню, а в игровом мире
- Проверьте, что используется правильная версия игры (DSR 1.03)
