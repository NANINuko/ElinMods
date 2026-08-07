# ElinMods

ElinMods は **NANINuko (なにぬぬこ)** が公開する Elin 用 Mod 集です。  
公開 Mod と個人用 Mod を同一リポジトリで管理しています。

## 重要
- 公開 Mod のみサポートします。個人用 Mod はサポート対象外です。  
- 各 Mod の使い方は各フォルダ内の `README.md` を参照してください。  
- 本リポジトリの公開物に関するバグ報告・要望は Issues で受け付けます（公開 Mod のみ対応）。

## 現在の公開 Mod
- `WorldWeave` — 野外マップ生成時のテンプレート建築配置 Mod

## 非公開 Mod
- `WellWish` — 願いがかなえられるようになると通知するMod

## インストール
### Steam Workshop
- Steam ワークショップページ: [https://steamcommunity.com/profiles/76561199610393202/myworkshopfiles/](https://steamcommunity.com/profiles/76561199610393202/myworkshopfiles/)

### 手動インストール（非推奨）
1. リポジトリをクローンまたはダウンロード  
   `git clone https://github.com/あなた/ElinMods.git`
2. 必要な Mod フォルダをゲームの `Elin/Package/` 配下にコピー

## 開発環境とビルド
- Visual Studio または `dotnet` を使用してください。  
- 例（WorldWeave をビルドする場合）:
  ```bash
  cd WorldWeave
  dotnet build
