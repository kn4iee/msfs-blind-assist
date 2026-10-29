const { test } = require('node:test');
const assert = require('node:assert');
const { load } = require('./run');

// Review 2026-10-29 (OIS M2): a colour the agent could not classify read "red", a neutral grey included, and red is a
// state: the engine cowls read Open, the brakes summary and painted buttons said red. A grey is grey; a colour that is
// none of the state colours has no word.
test('each state colour has its word, a grey is grey, and an unclassified colour none', () => {
  const { A } = load('ground');
  const words = {
    'rgb(0, 255, 0)': 'green', darkgreen: 'green', darkorange: 'amber', 'rgb(255, 191, 0)': 'amber',
    'rgb(153, 0, 0)': 'red', red: 'red', blue: 'blue', white: 'white', black: 'black',
    'rgb(128, 128, 128)': 'grey', 'rgb(100, 110, 105)': 'grey', 'rgb(255, 0, 255)': '',
  };
  for (const [c, word] of Object.entries(words)) assert.equal(A.colourWord(c), word, c);
});
