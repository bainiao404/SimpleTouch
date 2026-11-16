plugins {
    kotlin("jvm")
}

// 纯 Kotlin、零 Android 依赖（方案文档 §5）：与未来 KMP 复用及 PC 端语义保持一致
kotlin {
    jvmToolchain(17)
}

dependencies {
    testImplementation(kotlin("test"))
    // 仅测试用：解析 docs/protocol/test-vectors.json（双端同源向量）
    testImplementation("org.json:json:20240303")
}

tasks.withType<Test>().configureEach {
    useJUnitPlatform()
}
