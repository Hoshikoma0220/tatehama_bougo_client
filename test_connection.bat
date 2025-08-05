@echo off
echo ========================================
echo  立濱防護無線システム 接続テスト
echo ========================================
echo.

echo [1] サーバー状況確認中...
curl -s http://localhost:5233/ >nul 2>&1
if %errorlevel% equ 0 (
    echo ✅ サーバーは正常に動作しています
) else (
    echo ❌ サーバーが起動していません
    echo    サーバーを先に起動してください
    pause
    exit /b 1
)

echo.
echo [2] クライアントをビルド中...
cd /d "d:\tatehama\NEW\tatehama_bougo_client"
dotnet build --verbosity quiet
if %errorlevel% equ 0 (
    echo ✅ クライアントのビルドが完了しました
) else (
    echo ❌ クライアントのビルドに失敗しました
    pause
    exit /b 1
)

echo.
echo [3] クライアントを起動中...
echo    F4キーで防護無線の発砲/停止をテストできます
echo    列車番号: 現在の設定値を使用
echo    ゾーン: TrainCrewからの情報を使用
echo    デバッグ: Visual Studio出力ウィンドウで詳細ログを確認
echo.
echo ========================================
echo  テスト手順:
echo  1. F4キーを押して発砲開始
echo  2. 別のクライアントでも同様にテスト
echo  3. F4キーを再度押して発砲停止  
echo ========================================
echo.

start /wait bin\Debug\net8.0-windows\tatehama_bougo_client.exe

echo.
echo テスト完了
pause
