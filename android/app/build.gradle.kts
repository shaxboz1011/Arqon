plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
}

android {
    namespace = "app.arqon"
    compileSdk = 35

    defaultConfig {
        applicationId = "app.arqon"
        // Android 8.0: the first version with requestNetwork(request, callback, timeout), which is how each network is asked for.
        minSdk = 26
        targetSdk = 35
        versionCode = 2
        versionName = "0.2.0"
        // Where anonymous usage reports go (only if the user agrees). Empty = this build cannot send any: -PstatsUrl=https://...
        buildConfigField("String", "STATS_URL", "\"${project.findProperty("statsUrl") ?: ""}\"")
    }

    // The release key comes from the environment (the android-release workflow sets it); a local release build without it is unsigned.
    signingConfigs {
        create("release") {
            System.getenv("ARQON_KEYSTORE")?.let { path ->
                storeFile = file(path)
                storePassword = System.getenv("ARQON_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ARQON_KEY_ALIAS")
                keyPassword = System.getenv("ARQON_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            if (System.getenv("ARQON_KEYSTORE") != null) signingConfig = signingConfigs.getByName("release")
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
        buildConfig = true
    }
    packaging {
        resources.excludes += setOf("/META-INF/{AL2.0,LGPL2.1}", "META-INF/DEPENDENCIES")
    }
    lint {
        abortOnError = false
    }
}

dependencies {
    implementation(project(":core"))

    implementation(libs.core.ktx)
    implementation(libs.lifecycle.runtime)
    implementation(libs.lifecycle.runtime.compose)
    implementation(libs.lifecycle.viewmodel.compose)
    implementation(libs.activity.compose)
    implementation(libs.webkit)
    implementation(libs.coroutines.android)

    implementation(platform(libs.compose.bom))
    implementation(libs.compose.ui)
    implementation(libs.compose.ui.tooling.preview)
    implementation(libs.compose.material3)
    implementation(libs.compose.material.icons)
}
