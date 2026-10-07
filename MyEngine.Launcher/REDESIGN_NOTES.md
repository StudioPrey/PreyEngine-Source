# بازطراحی لانچر PreyEngine — یادداشت‌ها

این پوشه جایگزین کاملِ `src/MyEngine/src/MyEngine.Launcher` است (پوشه‌ی قبلی رو پاک کن و این رو بذار).

## منطق ساخت فایل
`Services/ProjectFactory.cs` و `Services/EditorVersionRegistry.cs` و `ViewModels/EditorVersionManagerViewModel.cs` **دست‌نخورده** هستن.
ساخت پوشه‌ها همچنان با `ProjectFactory.CreateProjectFolders` انجام میشه.

## فقط افزوده‌ها (بدون تغییر رفتار)
- `ProjectInfo`: دو فیلد جدید `CreatedAt` و `IsPinned` (پروژه‌های قدیمی بدون مشکل لود میشن)
- `ProjectRegistry.AddOrUpdate`: موقع ثبتِ پروژه‌ی جدید `CreatedAt` رو هم می‌نویسه
- `ProjectLauncher`: متدهای `Start` و `WaitForEditorAsync` اضافه شد؛ `Launch` همون کار قبلی رو می‌کنه
- سرویس و مدل جدید برای تنظیمات (`launcher-settings.json` کنار `projects.json`)

## حذف‌شده
- `Views/EditorVersionManagerWindow.*` (جاش صفحه‌ی «Editor versions» توی خودِ لانچره)
- فرم «New project» و «Open existing» سمت راست و دکمه‌ی «Manage Versions»

## ساختار تازه
- `Themes/Colors.axaml` پالت دارک/لایت — `Themes/Icons.axaml` لوگوی وکتور + آیکون‌ها — `Themes/ButtonThemes.axaml` و `Themes/Controls.axaml` استایل‌ها
- `Views/*View.axaml` هر صفحه (Projects, EditorVersions, Docs, Settings) + ویزارد (`NewProjectView`) + کارت لودینگ (`LaunchProgressView`)
- `Assets/logo.png` لوگوی جدید (سفید روی شفاف)، `Assets/app-icon.png` و `Assets/launcher.ico` آیکون برنامه

## تنظیمِ سریع
- حداقل زمانِ نمایشِ هر مرحله‌ی لودینگ: `StepPauseMs` در `MainWindowViewModel.cs`
- حداکثر انتظار برای پنجره‌ی ادیتور: `EditorWindowTimeout` در همان فایل
- مدت اسپلش: اعداد `HoldUntilAsync(...)` در `SplashWindowViewModel.cs`
