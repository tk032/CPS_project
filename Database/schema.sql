-- ============================================================
--  Smart Factory CPS - MySQL 스키마
--  MySQL Workbench 에서 실행하세요
-- ============================================================

CREATE DATABASE IF NOT EXISTS smart_factory
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE smart_factory;

-- 1. 지역 기준 정보
CREATE TABLE IF NOT EXISTS region_rule (
    region_code  INT          PRIMARY KEY,
    region_name  VARCHAR(20)  NOT NULL,
    is_active    TINYINT(1)   NOT NULL DEFAULT 1,
    created_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP
);

INSERT IGNORE INTO region_rule (region_code, region_name) VALUES
    (1, '서울'),
    (2, '대전'),
    (3, '대구'),
    (4, '부산'),
    (5, '기타');

-- 2. 박스 기본 이력
CREATE TABLE IF NOT EXISTS parcel (
    id           INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    box_id       INT           NOT NULL,
    region_code  INT           NOT NULL,
    region_name  VARCHAR(20)   NOT NULL,
    weight_kg    DECIMAL(5,1)  NOT NULL,
    sort_result  VARCHAR(20)   NOT NULL DEFAULT 'OK',
    sorted_at    DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (region_code) REFERENCES region_rule(region_code)
);

-- 3. 분류 이벤트 로그
CREATE TABLE IF NOT EXISTS sort_event (
    id           INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    parcel_id    INT           NOT NULL,
    event_type   VARCHAR(30)   NOT NULL,   -- RFID_READ / SORT_COMPLETE / REJECT
    event_detail VARCHAR(200)  NULL,
    occurred_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (parcel_id) REFERENCES parcel(id)
);

-- 4. 라인 상태 스냅샷 (30초마다 기록)
CREATE TABLE IF NOT EXISTS line_status_log (
    id                  INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    state_code          INT          NOT NULL DEFAULT 0,
    is_running          TINYINT(1)   NOT NULL DEFAULT 0,
    total_processed     INT          NOT NULL DEFAULT 0,
    throughput_per_min  DECIMAL(5,1) NOT NULL DEFAULT 0,
    logged_at           DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 5. 알람 이력
CREATE TABLE IF NOT EXISTS alarm_history (
    id              INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    alarm_code      INT          NOT NULL,
    alarm_message   VARCHAR(200) NOT NULL,
    occurred_at     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    resolved_at     DATETIME     NULL
);
