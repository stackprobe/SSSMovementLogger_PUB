# アプリアイコン

内蔵の image_gen ツールで、メインキャラクターの画像を参照して生成しました。

- 参照画像：`src/SSSMovementLogger/Resources/drawable-nodpi/neet_girl.png`
- 生成画像：`src/SSSMovementLogger/Resources/drawable-nodpi/ic_komori.png`（透明背景）
- 通常アイコン：淡いラベンダー背景に、ノートPCの前で頬杖をつくこもり。前景の余白はAndroidのdrawableで調整します。
- テーマアイコン：眠そうな顔とノートPCを表す単色ベクター。

生成プロンプト：

```text
Use case: stylized-concept. Asset type: Android adaptive app icon foreground on transparent background. Reference image shows the app's character Komori: adult woman with messy shoulder length lavender hair, half-lidded violet eyes, lazy teasing smile, mint hoodie. Draw a NEW simplified chibi anime icon of this SAME adult character, resting chin in one hand behind a small lavender laptop. Bold clear thick outlines, cute expressive sleepy face large and readable at 48px, flat cel shading, lavender and mint palette. Square canvas 1024 by 1024. Transparent background, no room, no text, no watermark, no border or rounded-square plate. CRITICAL adaptive icon safe zone: entire character plus laptop silhouette fits within central circle of diameter 60% of canvas, with generous transparent margins of at least 20% on all sides. Center face at x50% y44%, laptop at x50% y66%. Face should dominate the illustration; laptop simple clear. Not a full-body figure. Keep corners and all outside central circle fully transparent.
```
