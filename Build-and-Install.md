# ビルドとインストール

`Release.bat` で配布用の `out/SSSMovementLogger-Signed.apk` を作成します。通常のビルドでも `src/SSSMovementLogger/bin/{Debug|Release}/net9.0-android35.0/SSSMovementLogger-Signed.apk` を生成します。Release.ps1はこのAPKをoutへコピーします。

.NET 9 Androidワークロード、Android SDK Platform 35・Build Tools・Platform Tools、JDK 17以降が必要です。SDK/JDKは環境変数・一般的なインストール先・作業ルートの.build-toolsから検出し、不足時は導入を試みます。初回はネットワークと導入権限が必要です。

```powershell
.\Release.bat
```

SDK/JDKを指定する場合：

```powershell
.\Release.bat -AndroidSdkDirectory "C:\path\to\android-sdk" -JavaSdkDirectory "C:\path\to\jdk"
```

## インストール

1. APKをスマホへコピーし、ファイル管理アプリからインストールします。
2. 「ぐうたら日和」を開き、「設定」→「権限を設定」で位置情報を「常に許可」にします。おおよその位置でも利用できます。
3. 権限設定からアプリに戻ると自動で記録を開始します。「記録開始」でも開始できます。
4. こもりの通知を使いたい場合だけ「こもりからの通知」をオンにし、通知権限を許可します。

通知オフでは通知を発行せず、「記録中」などの常駐通知もありません。バックグラウンドでの位置情報の配信頻度はAndroidの制限を受けるため、記録・移動検出が遅れる場合があります。Google Play開発者サービスが必要です。

対象はAndroid 12以降のARM64端末、アプリIDはnet.sss.movementloggerです。TTTLATLONLoggerとは独立したアプリであり、そのアンインストールは不要です。

## 更新

同じアプリIDと署名鍵で作った新しいAPKを上書きインストールします。Release.ps1は通常、同じWindowsユーザーの.NET for Androidのデバッグ用キーストアを使います。別の鍵で作ったAPKでは上書き更新できません。元の鍵を使ってビルドしてください。

位置情報の権限がそろっていれば、更新前に停止していた場合も、更新・端末再起動後に自動で記録を開始します。新規インストールでは一度アプリを開き、位置情報を「常に許可」にしてアプリへ戻ってください。手動停止後も、次にアプリを起動するか設定画面を開き直すと自動開始します。

アンインストールやアプリデータ消去は記録・設定・権限を消去します。記録ファイルには期間・件数の自動削除はありませんが、端末の空き容量が必要です。
