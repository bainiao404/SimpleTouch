plugins {
    id("com.android.application")
    kotlin("android")
    kotlin("plugin.compose")
}

android {
    namespace = "com.simpletouch.app"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.simpletouch.app"
        minSdk = 28
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0-M0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
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
}
