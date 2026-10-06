let gridSize = 10;
let tileSize = 14; // 1/5 of original 70

let board = [];
let player;

let orcaImg = null;
let gazelleImg = null;
let iguanaImg = null;
let pantherImg = null;
let mapImg;

let borderTiles = [null, null, null, null];

let colors = [
  "rgb(255, 0, 0)",
  "rgb(255, 127, 0)",
  "rgb(255, 255, 0)",
  "rgb(0, 255, 0)",
  "rgb(0, 0, 255)",
  "rgb(75, 0, 130)",
  "rgb(255, 255, 255)"
];

let currentColor = 0;
let gameWon = false;

let moveDelay = 90;
let lastMove = 0;


function setup() {

  // Border
  createCanvas(
    (gridSize + 2) * tileSize,
    (gridSize + 2) * tileSize
  );

  noSmooth();
  imageMode(CORNER);

  loadAnimalImages();
  loadMap();

  startGame();
}


// Load map
function loadMap() {

  loadImage(
    "Map.png"
  );

}


// Load animal images
function loadAnimalImages() {

  loadImage(
    "orca.png",
    function(img) {
      orcaImg = img;
      borderTiles[0] = img;
    }
  );


  loadImage(
    "gazelle.png",
    function(img) {
      gazelleImg = img;
      borderTiles[1] = img;
    }
  );


  loadImage(
    "iguana.png",
    function(img) {
      iguanaImg = img;
      borderTiles[2] = img;
    }
  );


  loadImage(
    "panther.png",
    function(img) {
      pantherImg = img;
      borderTiles[3] = img;
    }
  );

}


// Game
function startGame() {

  board = [
    [0, 2, 6, 1, null, 3, 2, null, null, 0],
    [null, null, 4, 3, null, 4, 5, null, null, null],
    [3, null, 3, null, null, 2, 0, null, 2, null],
    [null, null, null, null, 3, 5, null, 4, null, null],
    [0, null, 4, null, null, 2, 6, null, 3, null],
    [null, 4, null, null, null, null, null, null, null, null],
    [1, null, 2, 4, null, 4, 6, null, null, 5],
    [null, 4, null, null, null, null, null, 2, null, null],
    [1, null, 5, null, null, 4, null, null, 6, 4],
    [0, 3, null, 6, null, 1, 5, null, null, 0]
  ];

  player = {
    x: floor(gridSize / 2),
    y: floor(gridSize / 2)
  };

  currentColor = 0;
  gameWon = false;
}


// Draw
function draw() {

  background(80, 80, 120);

  movePlayer();

  drawBorder();
  drawBoard();
  drawPlayer();

  if (gameWon) {
    drawWinScreen();
  }
}


// Border
function drawBorder() {

  let borderSize = gridSize + 2;

  let index = 0;


  for (let x = 0; x < borderSize; x++) {

    drawBorderTile(
      borderTiles[index % borderTiles.length],
      x * tileSize,
      0
    );

    index++;
  }


  for (let y = 1; y < borderSize; y++) {

    drawBorderTile(
      borderTiles[index % borderTiles.length],
      (borderSize - 1) * tileSize,
      y * tileSize
    );

    index++;
  }


  for (let x = borderSize - 2; x >= 0; x--) {

    drawBorderTile(
      borderTiles[index % borderTiles.length],
      x * tileSize,
      (borderSize - 1) * tileSize
    );

    index++;
  }


  for (let y = borderSize - 2; y > 0; y--) {

    drawBorderTile(
      borderTiles[index % borderTiles.length],
      0,
      y * tileSize
    );

    index++;
  }
}


function drawBorderTile(img, x, y) {

  if (img && img.width > 0) {

    image(
      img,
      x,
      y,
      tileSize,
      tileSize
    );

  } else {

    noStroke();

    fill(0, 0, 0);

    rect(
      x,
      y,
      tileSize,
      tileSize
    );
  }
}


// Player movement
function movePlayer() {

  if (gameWon) {
    return;
  }

  let moveX = 0;
  let moveY = 0;


  if (keyIsDown('a')) {

    moveX = -1;

  } else if (keyIsDown('d')) {

    moveX = 1;

  } else if (keyIsDown('w')) {

    moveY = -1;

  } else if (keyIsDown('s')) {

    moveY = 1;
  }


  // Slow down movement
  if (millis() - lastMove < moveDelay) {
    return;
  }


  let newX = player.x + moveX;
  let newY = player.y + moveY;


  // Stop leaving board
  if (
    newX < 0 ||
    newX >= gridSize ||
    newY < 0 ||
    newY >= gridSize
  ) {

    return;
  }


  let tile = board[newY][newX];


  // Empty space
  if (tile == null) {

    player.x = newX;
    player.y = newY;
  }


  // Correct color
  else if (tile == currentColor) {

    board[newY][newX] = null;

    player.x = newX;
    player.y = newY;

    currentColor++;


    // You win
    if (currentColor >= colors.length) {

      gameWon = true;
    }
  }


  // Slow down part 2
  lastMove = millis();
}


// Maze
function drawBoard() {

  for (let y = 0; y < gridSize; y++) {

    for (let x = 0; x < gridSize; x++) {

      // Make room
      let px = (x + 1) * tileSize;
      let py = (y + 1) * tileSize;


      // Tile outline
      stroke(38, 35, 80);
      strokeWeight(0.8);
      noFill();

      rect(
        px + 0.4,
        py + 0.4,
        tileSize - 0.8,
        tileSize - 0.8
      );


      if (board[y][x] != null) {

        let tileColor = colors[board[y][x]];

        noStroke();

        fill(tileColor);

        rect(
          px + 1.6,
          py + 1.6,
          tileSize - 3.2,
          tileSize - 3.2
        );


        // Shading
        fill(0, 0, 0, 70);

        rect(
          px + 1.6,
          py + tileSize - 3.2,
          tileSize - 3.2,
          1.6
        );

        rect(
          px + tileSize - 3.2,
          py + 1.6,
          1.6,
          tileSize - 3.2
        );


        // Highlights
        fill(255, 255, 255, 90);

        rect(
          px + 1.6,
          py + 1.6,
          tileSize - 3.2,
          1.2
        );

        rect(
          px + 1.6,
          py + 1.6,
          1.2,
          tileSize - 3.2
        );
      }
    }
  }
}


// Player
function drawPlayer() {

  // Move properly
  let px = (player.x + 1) * tileSize;
  let py = (player.y + 1) * tileSize;


  // Shading
  noStroke();

  fill(0, 0, 0, 120);

  rect(
    px + 3.4,
    py + 3.4,
    tileSize - 5.2,
    tileSize - 5.2
  );


  // Player color
  fill(colors[currentColor]);

  rect(
    px + 2.8,
    py + 2.8,
    tileSize - 5.6,
    tileSize - 5.6
  );


  // Player outline
  stroke(255);
  strokeWeight(1.4);
  noFill();

  rect(
    px + 2.8,
    py + 2.8,
    tileSize - 5.6,
    tileSize - 5.6
  );


  // Highlight
  noStroke();

  fill(255);

  rect(
    px + 4,
    py + 4,
    1.6,
    1.6
  );
}


// Win screen
function drawWinScreen() {

  noStroke();

  fill(8, 8, 12);

  rect(
    0,
    0,
    width,
    height
  );


  
  let scale = tileSize / 70;


  // old win screen
  // 380 x 200
  // 5 px border
  // 42 px title
  // 18 px subtitle

  let boxWidth = 380 * scale;
  let boxHeight = 200 * scale;

  let boxX = width / 2 - boxWidth / 2;
  let boxY = height / 2 - boxHeight / 2;


  // Main box
  stroke(255);
  strokeWeight(5 * scale);

  fill(20, 20, 30);

  rect(
    boxX,
    boxY,
    boxWidth,
    boxHeight
  );


  // Mini corners
  noStroke();

  fill(255);

  let cornerSize = 20 * scale;

  rect(
    boxX,
    boxY,
    cornerSize,
    cornerSize
  );

  rect(
    boxX + boxWidth - cornerSize,
    boxY,
    cornerSize,
    cornerSize
  );

  rect(
    boxX,
    boxY + boxHeight - cornerSize,
    cornerSize,
    cornerSize
  );

  rect(
    boxX + boxWidth - cornerSize,
    boxY + boxHeight - cornerSize,
    cornerSize,
    cornerSize
  );


  // Win screen text
  textAlign(CENTER, CENTER);

  textStyle(BOLD);

  noStroke();


  // Title
  textSize(42 * scale);

  fill(255);

  text(
    "Fell, Jerk, Thief",
    width / 2,
    height / 2 - (25 * scale)
  );


  // Subtitle
  textSize(18 * scale);

  fill(180);

  text(
    "Press 'E' to Restart",
    width / 2,
    height / 2 + (35 * scale)
  );
}


// Restart
function keyPressed() {

  if (key == "e" || key == "E") {

    startGame();
  }
}