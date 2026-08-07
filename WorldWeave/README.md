# WorldWeave

**WorldWeave** は野外マップ生成時にテンプレート建築を配置する Mod です。  
ノーコードでテンプレートを追加・カスタマイズでき、テンプレートの永続キャッシュ機能を備えています。

---

## インストール

### 推奨（Steam Workshop）
- Steam ワークショップからダウンロードしてください。  
  ワークショップページ: [https://steamcommunity.com/profiles/76561199610393202/myworkshopfiles/](https://steamcommunity.com/profiles/76561199610393202/myworkshopfiles/)

### 手動インストール（非推奨）
1. リポジトリをクローンまたはダウンロード  
   ```bash
   git clone git clone https://github.com/NANINuko/ElinMods.git
   ```
2. `WorldWeave` フォルダをゲームの `Elin/Package/` 配下にコピー

---

## 使い方
1. ゲームで任意の野外マップに入る。  
2. テンプレートが有効なら自動で配置を試行します。  
3. カスタムテンプレートを使う場合は「カスタマイズ」を参照してください。

---

## 設定例

Mod の設定ファイルで以下のように設定します。

```ini
# キャッシュ関連
UsePersistentCache = true
PurgeOldCachesOnReload = true
CacheRetentionDays = 30
LogCacheStatisticsOnReload = true

# デバッグ
EnableDetailedLogs = false

# 1マップあたりの試行テンプレート数
MaxTemplatesPerMap = 1
```

- **UsePersistentCache**: 展開済みテンプレートを永続キャッシュとして保存するか。  
- **PurgeOldCachesOnReload**: Mod 再読み込み時に古いキャッシュを削除するか。  
- **CacheRetentionDays**: この日数より古いキャッシュを削除（0 以下で無効）。  
- **LogCacheStatisticsOnReload**: 再読み込み時にキャッシュ統計をログ出力するか。  
- **EnableDetailedLogs**: 詳細ログを出力（デバッグ用、通常は `false`）。  
- **MaxTemplatesPerMap**: 1 マップあたりに配置を試みるテンプレート数の上限。

---

## カスタマイズ（テンプレート追加手順）

1. `WorldWeave.dll` と同じ階層に `Templates/` フォルダを作成。  
2. 以下のペアを配置：  
   - `yourtemplate.z`（Elin のマップ保存形式）  
   - `yourtemplate.json`（テンプレート定義、`.z` と同名にすること）

### JSON の例

```json
{
  "enabled": true,
  "spawnChance": 0.1,
  "weight": 1,
  "maxPerMap": 5,
  "replaceExisting": true,
  "zoneTypes": ["Zone_Field"],
  "biomes": [],
  "activeRect": { "x": 1, "z": 1, "w": 64, "h": 64 },
  "variants": [
    {
      "id": "well",
      "spawnChance": 0.10,
      "weight": 1,
      "maxPerMap": 2,
      "replaceExisting": true,
      "activeRect": { "x": 2, "z": 3, "w": 2, "h": 2 }
    }
  ]
}
```

### 主なフィールド説明
- **enabled**: テンプレートを有効化するか。  
- **spawnChance**: 生成テーブルに入る確率（0〜1）。  
- **weight**: 生成テーブル内での抽選重み。  
- **maxPerMap**: 同一テンプレートの同一マップでの上限。  
- **replaceExisting**: 既存の Card を置換するか。  
- **zoneTypes**: 対象ゾーン（例: `Zone_Field`）。  
- **biomes**: 配置を許可するバイオーム（空配列で制限なし）。  
- **activeRect**: 建築の範囲（`x,z` は開始点、`w,h` は幅・高さ）。  
- **variants**: 同一 `.z` から複数テンプレートを使う場合の個別設定（`id`, `spawnChance`, `weight`, `maxPerMap`, `replaceExisting`, `activeRect` 等を指定可能）。

---

## 注意事項
- `.z` と `.json` は **同名ペア** にしてください。  
- 大きな `.z` ファイルや多数のテンプレートはキャッシュ・ロード時間に影響します。  
- 詳細ログはデバッグ時のみ有効化してください（通常は無効推奨）。  
- テンプレートの配置や置換はゲーム内の既存オブジェクトに影響を与える可能性があります。バックアップを推奨します。

---
