namespace SmartFactoryCPS.Services;

public static class DbConfig
{
    // ── MySQL 접속 정보 ─────────────────────────────────────────────
    // 비밀번호가 있으면 Pwd= 뒤에 입력하세요
    public static string ConnectionString =>
        "Server=localhost;Port=3306;Database=smart_factory;" +
        "Uid=root;Pwd=0130;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None;";
}
