import { ModRegistrar } from "cs2/modding";
import { CurbsidePanel } from "./panel";
const register:ModRegistrar = registry => registry.append("GameTopLeft",CurbsidePanel);
export default register;
