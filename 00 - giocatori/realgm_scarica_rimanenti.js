// Incolla tutto questo nella Console di Edge (F12) mentre sei su una pagina di basketball.realgm.com
(async () => {
  const L = "Hunter-Tyson/132189 Kaleb-Tarczewski/24248 Baptiste-Chauveau/246859 Moses-Wright/121418 Isaiah-Miles/42846 Davide-Moretti/54312 Osvaldas-Olisevicius/25776 Simone-Ventura/251394 Troy-Caupain/58524 Luka-Brajkovic/94722 Francesco-Candussi/27638 Lajae-Jones/205948 Sean-McDermott/85117 Colbey-Ross/88865 Michele-Ruzzier/43322 Davide-Alviti/63977 Amedeo-Della-Valle/26524 Nongo-Ikangi/73422 Isaiah-Roby/86028 Mauro-Villa/196860 Giovanni-De-Nicolao/63985 Sasha-Grant/120955 Charlie-Moore/85907 Garrett-Nevels/58164 Lorenzo-Ambrosin/88504 Andrew-Andrews/30401 Milan-Barbitch/117643 Federico-Zampini/120209 Gregorio-Allinei/184661 Ike-Anigbogu/85735 Alessandro-Cappelletti/52843 Brekkott-Chapman/81002 Jeremy-Senglin/58051 Derrick-Alston-Jr/109463 Matteo-Baiocchi/232488 Tommaso-Baldasso/54302 Kessler-Edwards/88952".split(' ');
  const out = {}, err = {};
  for (let i = 0; i < L.length; i++) {
    const [n, id] = L[i].split('/');
    const u = 'https://basketball.realgm.com/player/' + n + '/Summary/' + id;
    try {
      const r = await fetch(u, {credentials: 'include'});
      const t = await r.text();
      if (r.status === 200 && /Player Profile/.test(t)) out[u] = t; else err[u] = 'HTTP ' + r.status;
    } catch (e) { err[u] = String(e); }
    console.log((i + 1) + '/' + L.length + ' ' + n + (err[u] ? ' ERRORE ' + err[u] : ' ok'));
    await new Promise(z => setTimeout(z, 1500));
  }
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([JSON.stringify({ok: out, err: err})], {type: 'application/json'}));
  a.download = 'realgm_sorgenti_2.json';
  document.body.appendChild(a); a.click(); a.remove();
  console.log('FATTO: ' + Object.keys(out).length + ' ok, ' + Object.keys(err).length + ' errori. File realgm_sorgenti_2.json in Download.');
})();
