/// <binding Clean='clean' />
"use strict";

var gulp = require("gulp"),
    rimraf = require("rimraf"),
    concat = require("gulp-concat"),
    cssmin = require("gulp-cssmin"),
    uglify = require("gulp-uglify"),
    less = require("gulp-less"),
    sourcemaps = require("gulp-sourcemaps"),
    debug = require("gulp-debug"),
    rename = require("gulp-rename"),
    ts = require("gulp-typescript");

var paths = {
    webroot: "./wwwroot/"
};
paths.less = paths.webroot + "css/**/*.less";
paths.js = paths.webroot + "js/**/*.js";
paths.libJs = paths.webroot + "lib/**/*.js";
paths.ts = paths.webroot + "js/**/*.ts";
paths.minJs = paths.webroot + "js/**/*.min.js";
paths.css = paths.webroot + "css/**/*.css";
paths.libCss = paths.webroot + "lib/**/*.css";
paths.minCss = paths.webroot + "css/**/*.min.css";
paths.concatJsDest = paths.webroot + "js/site.js";
paths.concatCssDest = paths.webroot + "css/site.css";


gulp.task("clean:js",
    ["ts"],
    function(cb) {
        rimraf(paths.concatJsDest, cb);
    });

gulp.task("clean:css",
    ["less"],
    function(cb) {
        rimraf(paths.concatCssDest, cb);
    });

gulp.task("clean", ["clean:js", "clean:css"]);

gulp.task("min:js",
    ["combine:js"],
    function() {
        return gulp.src([paths.js, "!" + paths.minJs], { base: "." })
            .pipe(debug())
            .pipe(sourcemaps.init({ loadMaps: true }))
            .pipe(uglify())
            .pipe(rename({ suffix: ".min" }))
            .pipe(sourcemaps.write("."))
            .pipe(gulp.dest(function(file) {
                return file.base;
            }));
    });

gulp.task("min:css",
    ["combine:css"],
    function() {
        return gulp.src([paths.css, "!" + paths.minCss])
            .pipe(debug())
            .pipe(sourcemaps.init({ loadMaps: true }))
            .pipe(cssmin())
            .pipe(rename({ suffix: ".min" }))
            .pipe(sourcemaps.write("."))
            .pipe(gulp.dest(function(file) {
                return file.base;
            }));
    });

gulp.task("min", ["min:js", "min:css"]);

gulp.task("combine:js",
    ["clean:js"]//,
    //function() {
    //    return gulp.src([paths.js, "!" + paths.minJs])
    //        .pipe(sourcemaps.init({ loadMaps: true }))
    //        .pipe(concat(paths.concatJsDest))
    //        .pipe(sourcemaps.write())
    //        .pipe(gulp.dest("."));
    //}
    );


gulp.task("combine:css",
    ["clean:css"],
    function() {
        return gulp.src([paths.css,"!" + paths.minCss])
            .pipe(sourcemaps.init({ loadMaps: true }))
            .pipe(concat(paths.concatCssDest))
            .pipe(sourcemaps.write())
            .pipe(gulp.dest("."));
    });

gulp.task("combine", ["combine:js", "combine:css"]);

gulp.task("less",
    function() {
        return gulp.src([paths.less])
            .pipe(debug())
            .pipe(sourcemaps.init({ loadMaps: true }))
            .pipe(less())
            .pipe(sourcemaps.write())
            .pipe(gulp.dest(function(file) {
                return file.base;
            }));
    });

gulp.task("ts",
    function() {
        return gulp.src([paths.ts])
            .pipe(debug())
            .pipe(sourcemaps.init())
            .pipe(ts({declarations:false}))
            .pipe(sourcemaps.write())
            .pipe(gulp.dest(function(file) {
                return file.base;
            }));

    });

gulp.task("default", ["build", "watch"]);

gulp.task("build", ["less", "ts", "clean", "combine", "min"]);
gulp.task("build:js", ["ts", "clean:js", "combine:js", "min:js"]);
gulp.task("build:css", ["less", "clean:css", "combine:css", "min:css"]);

gulp.task("watch",
    function() {
        gulp.watch(paths.less, ["build:css"]);
        gulp.watch(paths.ts, ["build:js"]);
    });

gulp.task("watch:css",
    function() {
        gulp.watch(paths.less, ["build:css"]);
    });

gulp.task("watch:js",
    function() {
        gulp.watch(paths.ts, ["build:js"]);
    });
