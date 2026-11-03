# VoiSonaTalkForElin

**VoiSonaTalkForElin** は、Elin の Mod **KK Elin With TTS** で VoiSona Talk を使用できるようにする追加 Mod です。

**公開Modではありませんので個別対応は致しかねます。**

VoiSona Talk のローカル API と連携し、音声合成を行います。現在は雨衣（標準）に対応しています。

---

## 概要

- **目的**: KK Elin With TTS に VoiSona Talk を追加する。
- **対応音声**: 雨衣（標準）。
- **音声合成**: VoiSona Talk のローカル API を利用。
- **Drama 対応**: 会話イベントの台詞読み上げに対応。
- **追加機能**:
  - `text_TTS` による読み上げテキストの個別指定。
  - `#nospeak` による読み上げスキップ。
  - 会話終了・切り替え時の読み上げ停止。

---

## 必要なもの

- Elin
- BepInEx
- KK Elin With TTS
- VoiSona Talk

---

## インストール

1. VoiSona Talk をインストールし、ローカル API を起動できる状態にする。
2. ビルド済み DLL（`VoiSonaTalkForElin.dll`）を用意する。
3. ゲームの Mod 配置先（例: `Elin/Package/`）にフォルダを作成し、DLL を配置する。
4. ゲームを起動して動作を確認する。

---

## 設定

初回起動後、BepInEx の設定ファイルから変更できます。

| 項目 | 初期値 | 説明 |
|---|---|---|
| ApiUrl | `http://127.0.0.1:32766` | VoiSona Talk API の接続先 |
| UserName | 空欄 | API の Basic 認証ユーザー名 |
| Password | 空欄 | API の Basic 認証パスワード |
| VoiceName | `ui_ja_JP` | 使用する音声 |
| VoiceVersion | `2.0.0` | 音声バージョン |
| Language | `ja_JP` | 音声合成言語 |
| PollIntervalMs | `250` | 合成状態の確認間隔（ミリ秒） |
| TimeoutSeconds | `120` | 音声合成のタイムアウト（秒） |

---

## 使い方

1. VoiSona Talk の API を起動する。
2. Elin を起動する。
3. KK Elin With TTS の設定からエンジンに **VoiSona Talk (雨衣)** を選択する。
4. キャラクターに音声を割り当てて使用する。

---

## 注意事項

- VoiSona Talk の API が起動していない場合、音声合成はできません。
- API の接続設定や認証情報が正しく設定されている必要があります。
- 音声合成には時間がかかる場合があります。
- KK Elin With TTS 側のアップデートによって動作しなくなる可能性があります。
- ゲームや他 Mod との競合については保証しません。

---
