# 開発ガイド

ユーザー向けの説明は [README](../README.md)、内部仕様は [設計](Design.md)、確認項目は [実装と確認](Implementation-Plan.md) にまとめています。APKの作成・導入・更新については [ビルドとインストール](../Build-and-Install.md) を参照してください。

## 開発環境と確認

.NET 9 Androidワークロード、Android SDK 35、JDK 17以降が必要です。Android 12以降のARM64端末向けです。アプリIDは `net.sss.movementlogger`、バージョンは0.6.0です。TTTLATLONLoggerの権限や記録は引き継ぎません。

```powershell
dotnet build src/SSSMovementLogger.sln -c Release
dotnet run --project src/Tests/SSSMovementLogger.Tests.csproj -c Release
```

作業ルート直下の `Clean.bat` と `Release.bat` は、ユーザーからそのファイルの変更を明示的に依頼された場合を除き、変更しないでください。

## セリフの編集

`src/SSSMovementLogger/Assets/dialogue.json` を編集し、再ビルドしてください。

| キー | 場面 |
| --- | --- |
| `active` | 移動の目安が3km以上 |
| `moving` | 500m以上3km未満 |
| `still` | 500m未満 |
| `insufficient` | 観測が不足 |
| `stale` | 最新の測位が15分以上前 |
| `started` | 長い滞在後の移動開始の通知 |
| `stopped` | 移動後の停止の通知 |
| `recording_paused` | 記録停止中の指摘（吹き出し） |

各配列に空でないセリフが必要です。`{distanceKm}` は距離の目安、`{samples}` は記録数、`{observedMinutes}` は欠測を除いた観測時間、`{displacementKm}` は最初と最後の位置の隔たり、`{lastFixMinutes}` は最後の測位からの経過分、`{coverageNote}` は欠測や誤差の補足に置き換わります。

キャラクター画像は `src/SSSMovementLogger/Resources/drawable-nodpi/neet_girl.png` です。

## 記録方式の要点

Google Play開発者サービスのバックグラウンド測位を利用し、取得間隔は5分を希望値として登録します。Androidによって取得・配信間隔が延びる場合があり、5分ごとの取得や即時配信は保証しません。常駐サービスや記録状態のシステム通知は使いません。

UTC時刻・緯度・経度を端末のバックアップ対象外領域へ追記保存します。期間・件数で自動削除せず、空き容量まで保存します。停止後も記録は残ります。権限がそろっていれば、停止していた場合もアプリ起動・設定画面への復帰・端末再起動・アプリ更新で自動開始します。

会話には直近3時間の記録を使います。遅れてまとめて配信された測位も保存しますが、過去のイベントを現在の通知として再生しません。保存形式や判定条件の詳細は [設計](Design.md) を参照してください。
