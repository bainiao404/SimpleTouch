import java.util.Properties

plugins {
    id("com.android.application")
    kotlin("android")
    kotlin("plugin.compose")
}

// M4 发布签名：keystore/keystore.properties 本地生成（不入库，见 .gitignore）；
// 缺失时回退 debug 签名，保证无 keystore 环境仍可构建 release 包（仅限开发验证）
val keystoreProps = Properties().apply {
    val f = rootProject.file("keystore/keystore.properties")
    if (f.exists()) f.inputStream().use { load(it) }
}

android {
    namespace = "com.simpletouch.app"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.simpletouch.app"
        minSdk = 28
        targetSdk = 35
        versionCode = 2
        versionName = "0.4.0" // 与 PC Agent <Version> 保持一致（发布检查清单项）
    }

    signingConfigs {
        if (keystoreProps.isNotEmpty()) {
            create("release") {
                storeFile = rootProject.file(keystoreProps.getProperty("storeFile"))
                storePassword = keystoreProps.getProperty("storePassword")
                keyAlias = keystoreProps.getProperty("keyAlias")
                keyPassword = keystoreProps.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            // M4 发布检查清单：release 包启用 R8 混淆+资源收缩，keep 协议模块（proguard-rules.pro）
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
            if (keystoreProps.isNotEmpty()) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        compose = true
    }
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.12.01"))
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.compose.material3:material3")

    implementation(project(":core:protocol"))
    implementation(project(":core:bluetooth"))
    implementation(project(":feature:touchpad"))
    implementation(project(":feature:hotkeys"))
    implementation(project(":feature:keyboard"))
}
