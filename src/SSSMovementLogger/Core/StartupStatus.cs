namespace SSSMovementLogger.Core;

internal sealed record StartupStatus(bool LocationPermission, bool BackgroundLocation,
    bool Notifications, bool LocationEnabled, bool BackgroundRestricted, bool BatteryExempt)
{
    public bool HasAutomaticLocationPermissions => LocationPermission && BackgroundLocation;

    public string MissingAutomaticPermissions => string.Join("、", new[]
    {
        LocationPermission ? null : "位置情報が未許可",
        BackgroundLocation ? null : "位置情報の「常に許可」が未許可"
    }.Where(value => value is not null));

    public string Describe() =>
        $"位置情報（おおよその位置で利用可能）：{(LocationPermission ? "許可済み" : "未許可（手動・自動開始に必要）")}\n" +
        $"位置情報の「常に許可」：{(BackgroundLocation ? "許可済み" : "未許可（自動開始に必要）")}\n" +
        $"端末の位置情報：{(LocationEnabled ? "ON" : "OFF（測位できません）")}\n" +
        $"通知権限：{(Notifications ? "許可済み／不要" : "未許可（記録開始は可能）")}\n" +
        $"バックグラウンド制限：{(BackgroundRestricted ? "あり（起動通知が届かない場合があります）" : "なし")}\n" +
        $"バッテリー最適化：{(BatteryExempt ? "除外済み" : "対象（これだけでは開始不可と判定しません）")}";
}
