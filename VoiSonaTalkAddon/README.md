\# VoiSona Talk for Elin



ElinのMod \*\*KK Elin With TTS\*\* で VoiSona Talk を使えるようにする追加Mod。



自分用に作ったものを公開しているだけなので、細かい説明は省略。



\## 機能



\- VoiSona TalkをTTSエンジンとして追加

\- 音声は雨衣（標準）に対応

\- キャラクターごとの音声設定に対応

\- Dramaの台詞読み上げに対応

\- `text\\\_TTS` による読み上げテキストの個別指定に対応

\- `#nospeak` による読み上げスキップに対応

\- 会話終了・切り替え時の読み上げ停止に対応



\## 必要なもの



\- Elin

\- BepInEx

\- \[KK Elin With TTS]

\- VoiSona Talk

\- VoiSona Talk API（ローカルサーバー）



\## 設定



初回起動後、BepInEx/config に生成される設定ファイルから変更可能。



| 項目 | 初期値 |

|---|---|

| ApiUrl | `http://127.0.0.1:32766` |

| UserName | 空欄 |

| Password | 空欄 |

| VoiceName | `ui\\\_ja\\\_JP` |

| VoiceVersion | `2.0.0` |

| Language | `ja\\\_JP` |

| PollIntervalMs | `250` |

| TimeoutSeconds | `120` |



UserName / Password はVoiSona Talk APIのBasic認証用。



\## 注意事項



\- VoiSona Talk側のAPIサーバーを起動しておく必要がある。

\- APIの接続設定が正しくない場合、音声合成はできない。

\- 音声合成には多少時間がかかる場合がある。



\## ライセンス



個人用に作成したModです。

