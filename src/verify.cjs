// Exercise the deployed watchdog methods with a controlled clock and timers.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const text = fs.readFileSync(process.argv[2], 'utf8');
const begin = text.indexOf('\tnoteReceiveActivity(cmd, uin) {');
const end = text.indexOf('\tsetOutboundHealthy(healthy) {', begin);
assert(begin > 0 && end > begin);
let now = 1000000;
let timers = [];
const context = {
  Date: class extends Date { static now() { return now; } },
  setTimeout(fn, delay) { const t = {fn, at: now + delay, unref() {}}; timers.push(t); return t; },
  clearTimeout(t) { timers = timers.filter(x => x !== t); },
  QQ_RECV_HEARTBEAT_CMD: 'heartbeat',
  RECEIVE_STALE_AFTER_MS: Number(text.match(/var RECEIVE_STALE_AFTER_MS = ([\de]+);/)[1]),
  RECEIVE_STALE_CONFIRM_MS: Number(text.match(/var RECEIVE_STALE_CONFIRM_MS = ([\de]+);/)[1]),
};
vm.createContext(context);
const Watch = vm.runInContext('(class {' + text.slice(begin, end) + '})', context);
const watch = new Watch();
Object.assign(watch, {disposed:false, connected:true, loggedIn:true, receiveWatchArmed:true,
  receiveWatchUin:'bot', _receiveHealthy:true, _outboundHealthy:true, log:{info(){},warn(){}}, emit(){}});
Object.defineProperty(watch, 'receiveHealthy', { get() { return this._receiveHealthy && this._outboundHealthy; }});
function advance(ms) {
  const until = now + ms;
  while (true) {
    timers.sort((a,b)=>a.at-b.at);
    const t = timers[0];
    if (!t || t.at > until) break;
    timers.shift(); now=t.at; t.fn();
  }
  now=until;
}
watch.noteReceiveActivity('message','bot');
advance(360000);
assert.equal(watch.receiveHealthy, true, 'normal six-minute silence remains healthy');
watch.noteReceiveActivity('message','bot');
advance(614999);
assert.equal(watch.receiveHealthy, true);
advance(1);
assert.equal(watch.receiveHealthy, false, 'confirmed prolonged silence still degrades');
watch.noteReceiveActivity('message','bot');
assert.equal(watch.receiveHealthy, true, 'actual receive traffic recovers health');
watch._outboundHealthy=false;
watch.noteReceiveActivity('message','bot');
assert.equal(watch.receiveHealthy, false, 'receive activity cannot hide outbound failure');
watch.connected=false;
assert.equal(watch.shouldWatchReceive(), false, 'disconnected sessions do not use idle watchdog');
console.log('PASS: normal idle, prolonged stall, recovery, outbound failure, disconnect');
