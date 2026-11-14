"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.dueDate = dueDate;
// A sample extension package that wraps dayjs, see docs/reference/extension-packages.md#sample-and-test-packages.
const dayjs_1 = __importDefault(require("dayjs"));
const utc_1 = __importDefault(require("dayjs/plugin/utc"));
dayjs_1.default.extend(utc_1.default);
function dueDate(input) {
    return { dueDate: dayjs_1.default.utc(input.from).add(30, "day").endOf("month").format("YYYY-MM-DD") };
}
