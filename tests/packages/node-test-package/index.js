"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.greet = greet;
const axis_test_dependency_1 = require("axis-test-dependency");
function greet(name) {
    return { greeting: (0, axis_test_dependency_1.greeting)(name) };
}
